using backend.Models;
using System.Threading.Tasks;

namespace backend.Services
{
	public class AdminService
	{
		private readonly AuthService _authService;

		public AdminService(AuthService authService)
		{
			_authService = authService;
		}

		public async Task<IEnumerable<LoginAttempt>> GetLoginAttempts()
		{
			return await _authService.GetLoginAttempts();
		}

		public async Task FlagIpAddress(string ipAddress, string reason)
		{
			await _authService.FlagIpAddress(ipAddress, reason);
		}

		public async Task UnflagIpAddress(string ipAddress)
		{
			await _authService.UnflagIpAddress(ipAddress);
		}

		public async Task<bool> EditDoctorAsync(string email, string password)
		{
			return await _authService.EditDoctorAsync(email, password);
		}
	}
}
