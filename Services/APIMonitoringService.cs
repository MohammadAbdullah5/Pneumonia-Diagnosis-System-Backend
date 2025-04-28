using System.Diagnostics;
using System.Collections.Concurrent;

namespace backend.Services
{
	public class MonitoringMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly ILogger<MonitoringMiddleware> _logger;
		private static readonly ConcurrentBag<ApiRequestLog> RequestLogs = new();

		// In-memory usage counters
		private static readonly ConcurrentDictionary<string, int> EndpointUsage = new();

		public MonitoringMiddleware(RequestDelegate next, ILogger<MonitoringMiddleware> logger)
		{
			_next = next;
			_logger = logger;
		}

		public async Task Invoke(HttpContext context)
		{
			var stopwatch = Stopwatch.StartNew();
			var path = context.Request.Path;
			var method = context.Request.Method;
			var ipAddress = context.Connection.RemoteIpAddress?.ToString();
			int statusCode = 0;

			try
			{
				await _next(context);
				statusCode = context.Response.StatusCode;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unhandled Exception at {Path}", path);
				statusCode = 500;
				throw;
			}
			finally
			{
				stopwatch.Stop();

				var log = new ApiRequestLog
				{
					Timestamp = DateTime.UtcNow,
					Endpoint = path,
					Method = method,
					IpAddress = ipAddress,
					ResponseStatus = statusCode,
					ResponseTimeMs = stopwatch.ElapsedMilliseconds
				};

				RequestLogs.Add(log);

				_logger.LogInformation("[{Timestamp}] {Method} {Path} {Status} from {IP} ({ElapsedMilliseconds}ms)",
					log.Timestamp, log.Method, log.Endpoint, log.ResponseStatus, log.IpAddress, log.ResponseTimeMs);
			}
		}

		// Optional: Expose Metrics
		public static IEnumerable<ApiRequestLog> GetRequestLogs() => RequestLogs.Reverse().Take(1000); // last 1000 logs
	}
	public class ApiRequestLog
	{
		public DateTime Timestamp { get; set; }
		public string Endpoint { get; set; }
		public string Method { get; set; }
		public string IpAddress { get; set; }
		public int ResponseStatus { get; set; }
		public long ResponseTimeMs { get; set; }
	}
}
