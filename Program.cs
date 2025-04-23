using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using System.Text;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
	c.SwaggerDoc("v1", new OpenApiInfo { Title = "Your API", Version = "v1" });

	// JWT auth config
	c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
	{
		In = ParameterLocation.Header,
		Description = "Enter your JWT token in the format: Bearer <your_token>",
		Name = "Authorization",
		Type = SecuritySchemeType.ApiKey,
		Scheme = "Bearer"
	});
	c.AddSecurityRequirement(new OpenApiSecurityRequirement
	{
		{
			new OpenApiSecurityScheme
			{
				Reference = new OpenApiReference
				{
					Type = ReferenceType.SecurityScheme,
					Id = "Bearer"
				}
			},
			new string[] {}
		}
	});
});
builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowFrontend",
		policy =>
		{
			policy.WithOrigins("http://localhost:5173") // Your frontend's URL
				  .AllowAnyHeader()
				  .AllowAnyMethod()
				  .AllowCredentials(); // If you're using cookies
		});
});


builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDBSettings"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
	var configuration = sp.GetRequiredService<IConfiguration>();
	var mongoSettings = configuration.GetSection("MongoDBSettings").Get<MongoDbSettings>();

	var client = new MongoClient(mongoSettings.ConnectionString);
	return client.GetDatabase(mongoSettings.DatabaseName);
});

builder.Services.AddRateLimiter(options =>
{
	// Fixed window limiter for general requests
	options.AddPolicy("fixed", httpContext =>
	{
		var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

		return RateLimitPartition.Get<string>(ip, _ =>
			new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
			{
				PermitLimit = 5,
				Window = TimeSpan.FromMinutes(1),
				QueueLimit = 2,
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst
			}));
	});

	// Token bucket limiter for authenticated routes
	options.AddPolicy("auth-policy", httpContext =>
	{
		var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

		return RateLimitPartition.Get<string>(ip, _ =>
			new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
			{
				TokenLimit = 10,
				TokensPerPeriod = 1,
				ReplenishmentPeriod = TimeSpan.FromSeconds(30),
				QueueLimit = 1,
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
				AutoReplenishment = true
			}));
	});

	// Sliding window limiter for AI diagnosis routes
	options.AddPolicy("ai-diagnosis", httpContext =>
	{
		var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

		return RateLimitPartition.Get<string>(ip, _ =>
			new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
			{
				PermitLimit = 5,
				Window = TimeSpan.FromMinutes(1),
				SegmentsPerWindow = 2,
				QueueLimit = 1,
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst
			}));
	});

	options.OnRejected = async (context, token) =>
	{
		context.HttpContext.Response.StatusCode = 429;
		await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", token);
	};
});

builder.Services.AddSingleton<UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton(provider =>
{
	var cloudinary = new Cloudinary(new Account(
		builder.Configuration["Cloudinary:CloudName"],
		builder.Configuration["Cloudinary:ApiKey"],
		builder.Configuration["Cloudinary:ApiSecret"]
	));
	return cloudinary;
});
builder.Services.AddScoped<DiagnosisService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.AddSingleton<IMongoCollection<User>>(sp =>
{
	var database = sp.GetRequiredService<IMongoDatabase>();
	return database.GetCollection<User>("Users");
});

builder.Services.AddSingleton<IMongoCollection<DiagnosisRequest>>(sp =>
{
	var database = sp.GetRequiredService<IMongoDatabase>();
	return database.GetCollection<DiagnosisRequest>("DiagnosisRequests");
});


var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"]);
builder.Services.AddAuthentication(options =>
{
	options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
	options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(x =>
{
	x.RequireHttpsMetadata = false;
	x.TokenValidationParameters = new TokenValidationParameters
	{
		ValidateIssuer = false,
		ValidateAudience = false,
		ValidateIssuerSigningKey = true,
		ValidateLifetime = true,
		IssuerSigningKey = new SymmetricSecurityKey(key)
	};
});
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.Run();
