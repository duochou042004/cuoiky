using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using SmartParking.Core.Data;
using SmartParking.Core.Models;
using SmartParking.Core.Utils;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SmartParking.Core.Services
{
    public class AuthService
    {
        private readonly MongoDBContext _context;
        private readonly ILogger<AuthService> _logger;
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;
        private readonly IDGeneratorService _idGeneratorService;

        public AuthService(
            MongoDBContext context, 
            ILogger<AuthService> logger, 
            IConfiguration configuration,
            EmailService emailService,
            IDGeneratorService idGeneratorService)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _emailService = emailService;
            _idGeneratorService = idGeneratorService;
        }

        /// <summary>
        /// Authenticate a user with username and password
        /// </summary>
        public async Task<(User user, string token)> AuthenticateAsync(string username, string password)
        {
            // Find user by username
            var user = await _context.Users
                .Find(u => u.Username == username && u.IsActive)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"Authentication failed: User {username} not found or inactive");
                return (null, null);
            }

            // Verify password
            if (!VerifyPassword(password, user.PasswordHash))
            {
                _logger.LogWarning($"Authentication failed: Invalid password for user {username}");
                return (null, null);
            }

            // Update last login time
            var update = Builders<User>.Update.Set(u => u.LastLogin, DateTime.UtcNow);
            await _context.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            // Generate JWT token
            var token = GenerateJwtToken(user);

            _logger.LogInformation($"User {username} authenticated successfully");
            return (user, token);
        }

        /// <summary>
        /// Create a new user
        /// </summary>
        public async Task<User> CreateUserAsync(User user, string password)
        {
            // Check if username already exists
            var existingUser = await _context.Users
                .Find(u => u.Username == user.Username)
                .FirstOrDefaultAsync();

            if (existingUser != null)
            {
                _logger.LogWarning($"User creation failed: Username {user.Username} already exists");
                throw new Exception($"Username {user.Username} already exists");
            }

            // Check if email already exists
            existingUser = await _context.Users
                .Find(u => u.Email == user.Email)
                .FirstOrDefaultAsync();

            if (existingUser != null)
            {
                _logger.LogWarning($"User creation failed: Email {user.Email} already exists");
                throw new Exception($"Email {user.Email} already exists");
            }

            // Generate employee ID if not provided
            if (string.IsNullOrEmpty(user.EmployeeId))
            {
                user.EmployeeId = await _idGeneratorService.GenerateEmployeeIdAsync(user.Role);
            }

            // Hash password
            user.PasswordHash = HashPassword(password);
            
            // Set creation time
            user.CreatedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            // Insert user
            await _context.Users.InsertOneAsync(user);

            _logger.LogInformation($"User {user.Username} created successfully with employee ID {user.EmployeeId}");
            return user;
        }

        /// <summary>
        /// Update an existing user
        /// </summary>
        public async Task<User> UpdateUserAsync(string id, User updatedUser)
        {
            // Find user by ID
            var user = await _context.Users
                .Find(u => u.Id == id)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"User update failed: User with ID {id} not found");
                throw new Exception($"User with ID {id} not found");
            }

            // Check if username is being changed and if it already exists
            if (user.Username != updatedUser.Username)
            {
                var existingUser = await _context.Users
                    .Find(u => u.Username == updatedUser.Username && u.Id != id)
                    .FirstOrDefaultAsync();

                if (existingUser != null)
                {
                    _logger.LogWarning($"User update failed: Username {updatedUser.Username} already exists");
                    throw new Exception($"Username {updatedUser.Username} already exists");
                }
            }

            // Check if email is being changed and if it already exists
            if (user.Email != updatedUser.Email)
            {
                var existingUser = await _context.Users
                    .Find(u => u.Email == updatedUser.Email && u.Id != id)
                    .FirstOrDefaultAsync();

                if (existingUser != null)
                {
                    _logger.LogWarning($"User update failed: Email {updatedUser.Email} already exists");
                    throw new Exception($"Email {updatedUser.Email} already exists");
                }
            }

            // Update user properties
            var update = Builders<User>.Update
                .Set(u => u.Username, updatedUser.Username)
                .Set(u => u.Email, updatedUser.Email)
                .Set(u => u.FirstName, updatedUser.FirstName)
                .Set(u => u.LastName, updatedUser.LastName)
                .Set(u => u.PhoneNumber, updatedUser.PhoneNumber)
                .Set(u => u.Role, updatedUser.Role)
                .Set(u => u.IsActive, updatedUser.IsActive)
                .Set(u => u.UpdatedAt, DateTime.UtcNow)
                .Inc(u => u.Version, 1);

            // Update user
            await _context.Users.UpdateOneAsync(u => u.Id == id, update);

            // Get updated user
            user = await _context.Users
                .Find(u => u.Id == id)
                .FirstOrDefaultAsync();

            _logger.LogInformation($"User {user.Username} updated successfully");
            return user;
        }

        /// <summary>
        /// Delete a user
        /// </summary>
        public async Task<bool> DeleteUserAsync(string id)
        {
            // Find user by ID
            var user = await _context.Users
                .Find(u => u.Id == id)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"User deletion failed: User with ID {id} not found");
                throw new Exception($"User with ID {id} not found");
            }

            // Delete user
            var result = await _context.Users.DeleteOneAsync(u => u.Id == id);

            _logger.LogInformation($"User {user.Username} deleted successfully");
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Change a user's password
        /// </summary>
        public async Task<bool> ChangePasswordAsync(string id, string currentPassword, string newPassword)
        {
            // Find user by ID
            var user = await _context.Users
                .Find(u => u.Id == id)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"Password change failed: User with ID {id} not found");
                throw new Exception($"User with ID {id} not found");
            }

            // Verify current password
            if (!VerifyPassword(currentPassword, user.PasswordHash))
            {
                _logger.LogWarning($"Password change failed: Invalid current password for user {user.Username}");
                throw new Exception("Current password is incorrect");
            }

            // Hash new password
            var passwordHash = HashPassword(newPassword);

            // Update password
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, passwordHash)
                .Set(u => u.UpdatedAt, DateTime.UtcNow)
                .Inc(u => u.Version, 1);

            var result = await _context.Users.UpdateOneAsync(u => u.Id == id, update);

            _logger.LogInformation($"Password changed successfully for user {user.Username}");
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Reset a user's password (admin function)
        /// </summary>
        public async Task<string> ResetPasswordAsync(string id)
        {
            // Find user by ID
            var user = await _context.Users
                .Find(u => u.Id == id)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"Password reset failed: User with ID {id} not found");
                throw new Exception($"User with ID {id} not found");
            }

            // Generate a random password
            var newPassword = GenerateRandomPassword();

            // Hash new password
            var passwordHash = HashPassword(newPassword);

            // Update password
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, passwordHash)
                .Set(u => u.UpdatedAt, DateTime.UtcNow)
                .Inc(u => u.Version, 1);

            await _context.Users.UpdateOneAsync(u => u.Id == id, update);

            _logger.LogInformation($"Password reset successfully for user {user.Username}");
            return newPassword;
        }

        /// <summary>
        /// Initiate password reset process by sending a reset token via email
        /// </summary>
        public async Task<bool> InitiatePasswordResetAsync(string email)
        {
            // Find user by email
            var user = await _context.Users
                .Find(u => u.Email == email && u.IsActive && u.Role == "ADMIN")
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"Password reset initiation failed: Admin user with email {email} not found or inactive");
                // Return true to prevent email enumeration attacks
                return true;
            }

            // Generate reset token
            var token = GeneratePasswordResetToken();
            var tokenExpiry = DateTime.UtcNow.AddHours(1); // Token valid for 1 hour

            // Update user with reset token
            var update = Builders<User>.Update
                .Set(u => u.PasswordResetToken, token)
                .Set(u => u.PasswordResetTokenExpiry, tokenExpiry)
                .Set(u => u.UpdatedAt, DateTime.UtcNow)
                .Inc(u => u.Version, 1);

            await _context.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            // Send password reset email
            await SendPasswordResetEmailAsync(user, token);

            _logger.LogInformation($"Password reset initiated for user {user.Username}");
            return true;
        }

        /// <summary>
        /// Complete password reset process using the token sent via email
        /// </summary>
        public async Task<bool> CompletePasswordResetAsync(string token, string newPassword)
        {
            // Find user by reset token
            var user = await _context.Users
                .Find(u => u.PasswordResetToken == token && 
                           u.PasswordResetTokenExpiry > DateTime.UtcNow && 
                           u.IsActive)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning("Password reset completion failed: Invalid or expired token");
                throw new Exception("Invalid or expired password reset token");
            }

            // Hash new password
            var passwordHash = HashPassword(newPassword);

            // Update password and clear reset token
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, passwordHash)
                .Set(u => u.PasswordResetToken, null)
                .Set(u => u.PasswordResetTokenExpiry, null)
                .Set(u => u.UpdatedAt, DateTime.UtcNow)
                .Inc(u => u.Version, 1);

            var result = await _context.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            _logger.LogInformation($"Password reset completed successfully for user {user.Username}");
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Get all users
        /// </summary>
        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users
                .Find(_ => true)
                .ToListAsync();
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        public async Task<User> GetUserByIdAsync(string id)
        {
            return await _context.Users
                .Find(u => u.Id == id)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Get user by username
        /// </summary>
        public async Task<User> GetUserByUsernameAsync(string username)
        {
            return await _context.Users
                .Find(u => u.Username == username)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Get user by email
        /// </summary>
        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .Find(u => u.Email == email)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Initialize the admin user if no users exist
        /// </summary>
        public async Task InitializeAdminUserAsync()
        {
            // Check if any users exist
            var userCount = await _context.Users.CountDocumentsAsync(FilterDefinition<User>.Empty);
            if (userCount > 0)
            {
                _logger.LogInformation("Admin user initialization skipped: Users already exist");
                return;
            }

            // Get admin user settings from configuration
            var adminSettings = _configuration.GetSection("AdminUser");
            var username = adminSettings["Username"] ?? "admin";
            var email = adminSettings["Email"] ?? "admin@smartparking.com";
            var password = adminSettings["Password"];
            var firstName = adminSettings["FirstName"] ?? "System";
            var lastName = adminSettings["LastName"] ?? "Administrator";

            if (string.IsNullOrWhiteSpace(password) || password == "CHANGE_ME")
            {
                _logger.LogWarning("Admin user initialization skipped: AdminUser:Password is not configured.");
                return;
            }

            // Create admin user
            var adminUser = new User
            {
                Username = username,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Role = "ADMIN",
                EmployeeId = "ADMIN001",
                IsActive = true
            };

            await CreateUserAsync(adminUser, password);
            _logger.LogInformation("Admin user initialized successfully");
        }

        #region Helper Methods

        /// <summary>
        /// Hash a password using BCrypt
        /// </summary>
        private string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        /// <summary>
        /// Verify a password against a hash using BCrypt
        /// </summary>
        private bool VerifyPassword(string password, string hash)
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }

        /// <summary>
        /// Generate a JWT token for a user
        /// </summary>
        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = _configuration["JwtSettings:Secret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("JwtSettings:Secret must be configured.");
            }

            var expiryHours = 8;
            if (int.TryParse(_configuration["JwtSettings:ExpiryHours"], out var configuredExpiryHours) && configuredExpiryHours > 0)
            {
                expiryHours = configuredExpiryHours;
            }

            var key = Encoding.ASCII.GetBytes(secret);
            
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("employeeId", user.EmployeeId)
                }),
                Expires = DateTime.UtcNow.AddHours(expiryHours),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        /// <summary>
        /// Generate a random password
        /// </summary>
        private string GenerateRandomPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*";
            var random = new Random();
            var password = new StringBuilder();

            // Generate a random password with at least 10 characters
            for (int i = 0; i < 10; i++)
            {
                password.Append(chars[random.Next(chars.Length)]);
            }

            return password.ToString();
        }

        /// <summary>
        /// Generate a password reset token
        /// </summary>
        private string GeneratePasswordResetToken()
        {
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes);
        }

        /// <summary>
        /// Send a password reset email
        /// </summary>
        private async Task SendPasswordResetEmailAsync(User user, string token)
        {
            var subject = "Smart Parking System - Password Reset";
            var resetUrl = $"{_configuration["AppSettings:BaseUrl"] ?? "http://localhost:3000"}/reset-password?token={token}";

            var htmlBody = $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #4CAF50; color: white; padding: 10px; text-align: center; }}
                    .content {{ padding: 20px; border: 1px solid #ddd; }}
                    .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #777; }}
                    .button {{ display: inline-block; background-color: #4CAF50; color: white; padding: 10px 20px; 
                              text-decoration: none; border-radius: 5px; margin: 20px 0; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>Password Reset Request</h1>
                    </div>
                    <div class='content'>
                        <p>Dear {user.FirstName} {user.LastName},</p>
                        <p>We received a request to reset your password for the Smart Parking System.</p>
                        <p>To reset your password, please click the button below:</p>
                        
                        <p style='text-align: center;'>
                            <a href='{resetUrl}' class='button'>Reset Password</a>
                        </p>
                        
                        <p>If you did not request a password reset, please ignore this email or contact the system administrator.</p>
                        <p>This password reset link will expire in 1 hour.</p>
                        
                        <p>Best regards,<br>Smart Parking System</p>
                    </div>
                    <div class='footer'>
                        <p>This is an automated email. Please do not reply to this message.</p>
                        <p>&copy; {DateTime.Now.Year} Smart Parking System. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>";

            await _emailService.SendEmailAsync(user.Email, $"{user.FirstName} {user.LastName}", subject, htmlBody);
        }

        #endregion
    }
}
