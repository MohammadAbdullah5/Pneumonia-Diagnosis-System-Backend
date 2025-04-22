using System.Net.Mail;
using System.Net;
using backend.Models;
using Microsoft.Extensions.Options;

namespace backend.Services
{
	public class EmailService : IEmailService
	{
		private readonly EmailSettings _settings;

		public EmailService(IOptions<EmailSettings> settings)
		{
			_settings = settings.Value;
		}

		public async Task SendEmailAsync(string to, string subject, string body)
		{
			using var message = new MailMessage();
			message.To.Add(new MailAddress(to));
			message.From = new MailAddress(_settings.SenderEmail, _settings.SenderName);
			message.Subject = subject;
			message.Body = body;
			message.IsBodyHtml = true;

			var smtpClient = new SmtpClient(_settings.SmtpServer, _settings.Port)  // Replace with your SMTP server
			{
				Port = 587,
				Credentials = new NetworkCredential(_settings.Username, _settings.Password),
				EnableSsl = true,
			};

			await smtpClient.SendMailAsync(message);
		}
	}

}
