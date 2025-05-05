using System.Diagnostics;
using System.Collections.Concurrent;

namespace backend.Services
{
    public class MonitoringMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MonitoringMiddleware> _logger;

        // Request logs (global, for admin console view)
        private static readonly ConcurrentBag<ApiRequestLog> RequestLogs = new();

        // Per-IP history for suspicious activity detection
        private static readonly ConcurrentDictionary<string, List<ApiRequestLog>> RequestHistory = new();

        // Suspicious IPs that are blocked
        private static readonly ConcurrentDictionary<string, SuspiciousIP> SuspiciousIPs = new();

        public MonitoringMiddleware(RequestDelegate next, ILogger<MonitoringMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // 💥 Block if already flagged
            if (SuspiciousIPs.ContainsKey(ip))
            {
                context.Response.StatusCode = 403; // Forbidden
                await context.Response.WriteAsync("Access Denied. Your activity was flagged as suspicious.");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var path = context.Request.Path;
            var method = context.Request.Method;
            var userAgent = context.Request.Headers["User-Agent"].ToString() ?? "unknown";

            try
            {
                await _next(context); // Proceed with request

                var statusCode = context.Response.StatusCode;
                var contentLength = context.Response.ContentLength ?? 0;

                TrackRequest(ip, path, method, userAgent, statusCode, stopwatch.ElapsedMilliseconds, contentLength);
                DetectSuspicious(ip);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled Exception at {Path}", path);
                throw;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        private void TrackRequest(string ip, string path, string method, string userAgent, int statusCode, long responseTimeMs, long contentLength)
        {
            var log = new ApiRequestLog
            {
                Timestamp = DateTime.UtcNow,
                Endpoint = path,
                Method = method,
                IpAddress = ip,
                ResponseStatus = statusCode,
                ResponseTimeMs = responseTimeMs,
                UserAgent = userAgent,
                ContentLength = contentLength
            };

            // Save globally for admin console
            RequestLogs.Add(log);

            // Save per-IP history
            RequestHistory.AddOrUpdate(
                ip,
                new List<ApiRequestLog> { log },
                (key, existingLogs) =>
                {
                    existingLogs.Add(log);
                    // Keep last 100 logs per IP to save memory
                    if (existingLogs.Count > 100) 
                        existingLogs.RemoveAt(0);
                    return existingLogs;
                });
        }

        private void DetectSuspicious(string ip)
        {
            if (!RequestHistory.ContainsKey(ip))
                return;

            var logs = RequestHistory[ip];

            var lastMinuteLogs = logs.Where(r => (DateTime.UtcNow - r.Timestamp).TotalSeconds <= 60).ToList();

            // 1. Too many requests in 1 min
            if (lastMinuteLogs.Count > 100)
            {
                FlagIp(ip, "Rate limit exceeded (over 100 requests/min)");
                return;
            }

            // 2. Repeated Unauthorized (401)
            if (logs.Where(r => r.ResponseStatus == 401).Count() > 5)
            {
                FlagIp(ip, "Repeated failed login attempts (over 5 times)");
                return;
            }

            // 3. Frequent Server Errors (5xx)
            if (logs.Where(r => r.ResponseStatus >= 500).Count() > 5)
            {
                FlagIp(ip, "Frequent server errors (5xx responses)");
                return;
            }

            // 4. Suspicious Methods
            if (logs.Any(r => r.Endpoint.Contains("/login") && (r.Method == "DELETE" || r.Method == "PUT")))
            {
                FlagIp(ip, "Abnormal HTTP method on login endpoint");
                return;
            }

            // 5. Suspicious URL Patterns
            if (logs.Any(r => r.Endpoint.Contains("../") || r.Endpoint.Contains("%00") || r.Endpoint.Contains("<script>")))
            {
                FlagIp(ip, "Possible path traversal or XSS attempt");
                return;
            }

            // 6. User-Agent missing or bot-like
            if (logs.Any(r => string.IsNullOrWhiteSpace(r.UserAgent) || r.UserAgent.Contains("curl") || r.UserAgent.Contains("bot")))
            {
                FlagIp(ip, "Suspicious User-Agent detected");
                return;
            }

            // 7. Large unexpected uploads
            if (logs.Any(r => r.ContentLength > 5_000_000))
            {
                FlagIp(ip, "Large unexpected upload (>5MB)");
                return;
            }
        }

        private void FlagIp(string ip, string reason)
        {
            _logger.LogWarning("Flagged IP: {IP} - {Reason}", ip, reason);
            SuspiciousIPs[ip] = new SuspiciousIP { Reason = reason, FlaggedAt = DateTime.UtcNow };
        }

        // Optional API for frontend to read logs
        public static IEnumerable<ApiRequestLog> GetRequestLogs() => RequestLogs.Reverse().Take(1000); // Latest 1000

        public static IEnumerable<string> GetFlaggedIPs() => SuspiciousIPs.Keys;
    }

    public class ApiRequestLog
    {
        public DateTime Timestamp { get; set; }
        public string Endpoint { get; set; }
        public string Method { get; set; }
        public string IpAddress { get; set; }
        public int ResponseStatus { get; set; }
        public long ResponseTimeMs { get; set; }
        public string UserAgent { get; set; }
        public long ContentLength { get; set; }
    }

    public class SuspiciousIP
    {
        public string Reason { get; set; }
        public DateTime FlaggedAt { get; set; }
    }
}
