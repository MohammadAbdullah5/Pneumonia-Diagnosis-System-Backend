using backend.Models;
using MongoDB.Driver;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace backend.Services
{
	public class FlaggedIpCleanupService : IHostedService, IDisposable
	{
		private readonly ILogger<FlaggedIpCleanupService> _logger;
		private readonly IMongoCollection<FlaggedIp> _flaggedIpCollection;
		private Timer _timer;

		// Change the constructor to inject IMongoDatabase instead of IMongoClient
		public FlaggedIpCleanupService(ILogger<FlaggedIpCleanupService> logger, IMongoDatabase mongoDatabase)
		{
			_logger = logger;
			_flaggedIpCollection = mongoDatabase.GetCollection<FlaggedIp>("FlaggedIps");
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			// Clean up every hour (you can adjust this to your needs)
			_timer = new Timer(CleanUpFlaggedIps, null, TimeSpan.Zero, TimeSpan.FromHours(1));
			return Task.CompletedTask;
		}

		private async void CleanUpFlaggedIps(object state)
		{
			try
			{
				var expirationTime = DateTime.UtcNow.AddHours(-24);
				var result = await _flaggedIpCollection.DeleteManyAsync(
					ip => ip.FlaggedAt < expirationTime
				);

				_logger.LogInformation($"Cleaned up {result.DeletedCount} expired flagged IPs.");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error occurred during flagged IP cleanup.");
			}
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_timer?.Change(Timeout.Infinite, 0);
			return Task.CompletedTask;
		}

		public void Dispose()
		{
			_timer?.Dispose();
		}
	}
}
