using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using System;
using System.Threading.Tasks;

namespace SmartParking.Core.Controllers
{
    [Route("api/users")]
    [ApiController]
    [Authorize(Roles = "ADMIN")]
    public class UserController : ControllerBase
    {
        private readonly AuthService _authService;
        private readonly ILogger<UserController> _logger;

        public UserController(AuthService authService, ILogger<UserController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        /// <summary>
        /// Get all users
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                var users = await _authService.GetAllUsersAsync();
                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all users");
                return StatusCode(500, new { error = "An error occurred while retrieving users" });
            }
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(string id)
        {
            try
            {
                var user = await _authService.GetUserByIdAsync(id);

                if (user == null)
                {
                    return NotFound(new { error = $"User with ID {id} not found" });
                }

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user with ID {id}");
                return StatusCode(500, new { error = "An error occurred while retrieving the user" });
            }
        }

        /// <summary>
        /// Create a new user
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            try
            {
                var user = new User
                {
                    Username = request.Username,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    PhoneNumber = request.PhoneNumber,
                    Role = request.Role,
                    IsActive = true
                };

                var createdUser = await _authService.CreateUserAsync(user, request.Password);
                return CreatedAtAction(nameof(GetUserById), new { id = createdUser.Id }, createdUser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing user
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
        {
            try
            {
                var user = await _authService.GetUserByIdAsync(id);

                if (user == null)
                {
                    return NotFound(new { error = $"User with ID {id} not found" });
                }

                // Update user properties
                user.Username = request.Username;
                user.Email = request.Email;
                user.FirstName = request.FirstName;
                user.LastName = request.LastName;
                user.PhoneNumber = request.PhoneNumber;
                user.Role = request.Role;
                user.IsActive = request.IsActive;

                var updatedUser = await _authService.UpdateUserAsync(id, user);
                return Ok(updatedUser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user with ID {id}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Delete a user
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                var result = await _authService.DeleteUserAsync(id);

                if (result)
                {
                    return Ok(new { message = $"User with ID {id} deleted successfully" });
                }
                else
                {
                    return BadRequest(new { error = $"Failed to delete user with ID {id}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting user with ID {id}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Reset a user's password (admin function)
        /// </summary>
        [HttpPost("{id}/reset-password")]
        public async Task<IActionResult> ResetPassword(string id)
        {
            try
            {
                await _authService.ResetPasswordAsync(id);
                return Ok(new { message = "Password reset successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting password for user with ID {id}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class CreateUserRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }
        public string Role { get; set; } // "ADMIN" or "STAFF"
    }

    public class UpdateUserRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }
        public string Role { get; set; } // "ADMIN" or "STAFF"
        public bool IsActive { get; set; }
    }
}
