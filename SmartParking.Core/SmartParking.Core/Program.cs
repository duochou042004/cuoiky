using Microsoft.OpenApi.Models;
using SmartParking.Core.Utils;
using SmartParking.Core.Data;
using SmartParking.Core.Services;
using SmartParking.Core.Hubs;
using SmartParking.Core.Middleware;
using MongoDB.Driver;
using Microsoft.AspNetCore.Cors;
using Microsoft.Extensions.FileProviders;
using System.IO;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Configure the web server to use the same local port as launchSettings and the Vite proxy.
builder.WebHost.UseUrls("http://localhost:5125");

// Đảm bảo mô hình ML.NET được sao chép vào thư mục bin
EnsureMLModelExists();

// Ensure debug frames directory exists
string debugFramesDir = Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames");
if (!Directory.Exists(debugFramesDir))
{
    Directory.CreateDirectory(debugFramesDir);
    Console.WriteLine($"Created debug frames directory: {debugFramesDir}");
}

// Thêm dịch vụ Controller
builder.Services.AddControllers();

// Thêm SignalR
builder.Services.AddSignalR();

// Rate limiting — protect the login endpoint from brute-force attempts.
// A fixed window of 5 attempts per minute, partitioned by client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// CORS — restrict to an explicit allow-list when configured.
// Set "Cors:AllowedOrigins" (array) in configuration to lock down origins.
// If left unset, fall back to the previous permissive behaviour so existing
// local/dev setups keep working; production should always set the allow-list.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        if (allowedOrigins != null && allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            // No allow-list configured — reflect any origin (dev fallback).
            policy.SetIsOriginAllowed(_ => true);
        }
    });
});

// Cấu hình Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SmartParking API", Version = "v1" });
    c.OperationFilter<SwaggerFileOperationFilter>(); // Đăng ký bộ lọc hỗ trợ file upload

    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
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
            Array.Empty<string>()
        }
    });
});

// Đăng ký MongoDB Context
builder.Services.AddSingleton<MongoDBContext>();

// Đăng ký MongoDB Schema Fix
builder.Services.AddSingleton<FixMongoDBSchema>();
builder.Services.AddSingleton<FixMonthlyVehicleSchema>();

// Đăng ký Database Index Manager
builder.Services.AddSingleton<DatabaseIndexManager>();

// Đăng ký MongoDB Cleanup Utility
builder.Services.AddSingleton<MongoDBCleanupUtility>();

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.ASCII.GetBytes(jwtSettings["Secret"] ?? "SmartParkingSecretKey123456789012345678901234");
var jwtIssuer = jwtSettings["Issuer"] ?? "SmartParkingAPI";
var jwtAudience = jwtSettings["Audience"] ?? "SmartParkingClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// Đăng ký các dịch vụ
builder.Services.AddSingleton<MLModelPrediction>(sp =>
    new MLModelPrediction(sp.GetRequiredService<ILogger<MLModelPrediction>>()));
builder.Services.AddSingleton<VehicleClassificationService>();
builder.Services.AddScoped<IDGeneratorService>();
builder.Services.AddScoped<ParkingService>();
builder.Services.AddScoped<LicensePlateService>();
builder.Services.AddScoped<ParkingFeeService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<MomoPaymentService>();
builder.Services.AddScoped<StripePaymentService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<MonthlyVehicleService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ReportExportService>();

// Đăng ký background service cho camera monitoring
builder.Services.AddHostedService<CameraMonitoringService>();

// Đăng ký background service cho maintenance tasks
builder.Services.AddHostedService<MaintenanceService>();

// Đăng ký background service cho transaction maintenance
builder.Services.AddHostedService<TransactionMaintenanceService>();

// Đăng ký HttpClient
builder.Services.AddHttpClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartParking API v1"));
}
else
{
    // Use global exception handling middleware in production
    app.UseGlobalExceptionHandling();
}

app.UseHttpsRedirection();

// Sử dụng CORS
app.UseCors("CorsPolicy");

// Enforce rate limiting policies (e.g. the "login" policy on AuthController).
app.UseRateLimiter();

// Add static files middleware for debug frames.
// These frames contain license-plate imagery and are served without authentication,
// so they are only exposed in Development (a debugging aid). In production the
// endpoint is disabled entirely to avoid leaking captured plate images.
if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(
            Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames")),
        RequestPath = "/DebugFrames"
    });
}

// Add static files middleware for invoices
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "Invoices")),
    RequestPath = "/Invoices"
});

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Map SignalR hub
app.MapHub<ParkingHub>("/parkingHub");

// One-time database maintenance (schema migrations + duplicate cleanup).
// These used to run on EVERY startup, which is wasteful and risky. They are now
// gated behind a flag so they only run when explicitly requested:
//   dotnet run -- --run-maintenance
// (or set "RunStartupMaintenance": true in configuration for one boot).
bool runMaintenance = args.Contains("--run-maintenance")
    || builder.Configuration.GetValue<bool>("RunStartupMaintenance");

if (runMaintenance)
{
    Console.WriteLine("Running one-time database maintenance (schema fixes + duplicate cleanup)...");

    // Fix MongoDB schema
    var mongoSchemaFix = app.Services.GetRequiredService<FixMongoDBSchema>();
    await mongoSchemaFix.FixTransactionSchema();

    // Fix MonthlyVehicles schema
    var monthlyVehicleSchemaFix = app.Services.GetRequiredService<FixMonthlyVehicleSchema>();
    await monthlyVehicleSchemaFix.FixMonthlyVehiclesSchema();

    // Clean up duplicate records in MongoDB collections
    try
    {
        var mongoDBCleanupUtility = app.Services.GetRequiredService<MongoDBCleanupUtility>();

        // Fix the specific M001 duplicate issue
        await mongoDBCleanupUtility.FixM001DuplicateAsync();

        // Only clean up vehicles for now, as we know there are duplicates there
        await mongoDBCleanupUtility.CleanupDuplicateVehiclesAsync();

        // Skip other collections for now as they might have schema issues
        // await mongoDBCleanupUtility.CleanupDuplicateTransactionsAsync();
        // await mongoDBCleanupUtility.CleanupDuplicateMonthlyVehiclesAsync();
        // await mongoDBCleanupUtility.CleanupDuplicateParkingSlotsAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error cleaning up duplicate records: {ex.Message}");
    }

    Console.WriteLine("Database maintenance complete.");
}
else
{
    Console.WriteLine("Skipping startup database maintenance. Run with '--run-maintenance' to migrate/clean up duplicates once.");
}

// Create database indexes (idempotent — safe to run on every startup)
var databaseIndexManager = app.Services.GetRequiredService<DatabaseIndexManager>();
await databaseIndexManager.CreateIndexesAsync();

// Initialize system settings
try
{
    using var scope = app.Services.CreateScope();
    var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
    await settingsService.InitializeSettingsAsync();
    Console.WriteLine("System settings initialized successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"Error initializing system settings: {ex.Message}");
}

// Initialize admin user
try
{
    using var scope = app.Services.CreateScope();
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.InitializeAdminUserAsync();
    Console.WriteLine("Admin user initialized successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"Error initializing admin user: {ex.Message}");
}

app.Run();

// Copy ML model from source (MLModels/) to bin output directory so it can be found at runtime.
// Non-fatal: if the model is absent the app falls back to heuristic vehicle classification.
void EnsureMLModelExists()
{
    try
    {
        string sourceModelPath = Path.Combine(Directory.GetCurrentDirectory(), "MLModels", "VehicleClassification.zip");
        string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MLModels");
        string targetModelPath = Path.Combine(targetDir, "VehicleClassification.zip");

        if (!File.Exists(sourceModelPath))
        {
            Console.WriteLine("ML model not found at MLModels/VehicleClassification.zip — running without ML classification.");
            return;
        }

        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        File.Copy(sourceModelPath, targetModelPath, overwrite: true);
        Console.WriteLine($"ML model copied to bin output: {targetModelPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: could not copy ML model — {ex.Message}. Continuing without ML classification.");
    }
}
