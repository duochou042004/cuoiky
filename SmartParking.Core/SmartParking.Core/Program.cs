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

var builder = WebApplication.CreateBuilder(args);

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

// Thêm CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy",
        policy =>
        {
            policy.AllowAnyMethod().AllowAnyHeader();

            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins).AllowCredentials();
            }
            else if (builder.Environment.IsDevelopment())
            {
                policy.WithOrigins("http://localhost:3000", "http://localhost:5173").AllowCredentials();
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
var jwtSecret = jwtSettings["Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException("JwtSettings:Secret must be configured.");
}

var key = Encoding.ASCII.GetBytes(jwtSecret);

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
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

// Đăng ký các dịch vụ
builder.Services.AddSingleton<MLModelPrediction>();
builder.Services.AddSingleton<VehicleClassificationService>();
builder.Services.AddScoped<IDGeneratorService>();
builder.Services.AddScoped<ParkingService>();
builder.Services.AddHttpClient<LicensePlateService>();
builder.Services.AddScoped<ParkingFeeService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<MomoPaymentService>();
builder.Services.AddScoped<StripePaymentService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<MonthlyVehicleService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuthService>();

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

// Add static files middleware for debug frames
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames")),
    RequestPath = "/DebugFrames"
});

// Add static files middleware for invoices
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "Invoices")),
    RequestPath = "/Invoices"
});

// Add static files middleware for reports
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "Reports")),
    RequestPath = "/Reports"
});

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Map SignalR hub
app.MapHub<ParkingHub>("/parkingHub");

// Fix MongoDB schema before running the application
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

// Create database indexes
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

// Hàm để đảm bảo mô hình ML.NET tồn tại trong thư mục bin
void EnsureMLModelExists()
{
    try
    {
        string sourceModelPath = Path.Combine(Directory.GetCurrentDirectory(), "MLModels", "VehicleClassification.zip");
        string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MLModels");
        string targetModelPath = Path.Combine(targetDir, "VehicleClassification.zip");

        Console.WriteLine($"Checking if model exists at source: {sourceModelPath}");
        if (File.Exists(sourceModelPath))
        {
            Console.WriteLine($"Source model exists. Ensuring target directory exists: {targetDir}");
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                Console.WriteLine($"Created target directory: {targetDir}");
            }

            Console.WriteLine($"Copying model to: {targetModelPath}");
            File.Copy(sourceModelPath, targetModelPath, true);
            Console.WriteLine($"Model copied successfully to: {targetModelPath}");
        }
        else
        {
            Console.WriteLine($"Source model not found at: {sourceModelPath}");
            // Check if model exists at absolute path
            string absoluteModelPath = "/home/user/ProjectITS/SmartParking.Core/SmartParking.Core/MLModels/VehicleClassification.zip";
            if (File.Exists(absoluteModelPath))
            {
                Console.WriteLine($"Model found at absolute path: {absoluteModelPath}");
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Console.WriteLine($"Created target directory: {targetDir}");
                }

                Console.WriteLine($"Copying model to: {targetModelPath}");
                File.Copy(absoluteModelPath, targetModelPath, true);
                Console.WriteLine($"Model copied successfully to: {targetModelPath}");
            }
            else
            {
                Console.WriteLine($"Model not found at absolute path either: {absoluteModelPath}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error ensuring ML model exists: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}
