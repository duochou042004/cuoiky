using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartParking.Core.Services;

namespace SmartParking.Core.Controllers
{
    [Route("api/reports")]
    [ApiController]
    [Authorize]
    public class ReportController : ControllerBase
    {
        private readonly ReportExportService _reportExportService;
        private readonly ILogger<ReportController> _logger;

        public ReportController(ReportExportService reportExportService, ILogger<ReportController> logger)
        {
            _reportExportService = reportExportService;
            _logger = logger;
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactionReport(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? paymentMethod,
            [FromQuery] string? searchTerm,
            [FromQuery] string? status)
        {
            try
            {
                var report = await _reportExportService.GetTransactionReportAsync(startDate, endDate, paymentMethod, searchTerm, status);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating transaction report");
                return StatusCode(500, new { error = "Không thể tạo báo cáo giao dịch." });
            }
        }

        [HttpGet("revenue")]
        public async Task<IActionResult> GetRevenueReport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            try
            {
                var report = await _reportExportService.GetRevenueReportAsync(startDate, endDate);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating revenue report");
                return StatusCode(500, new { error = "Không thể tạo báo cáo doanh thu." });
            }
        }

        [HttpGet("monthly-subscriptions")]
        public async Task<IActionResult> GetMonthlySubscriptionsReport(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? paymentMethod,
            [FromQuery] string? vehicleType,
            [FromQuery] string? status,
            [FromQuery] string? searchTerm)
        {
            try
            {
                var report = await _reportExportService.GetMonthlySubscriptionReportAsync(startDate, endDate, paymentMethod, vehicleType, status, searchTerm);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating monthly subscription report");
                return StatusCode(500, new { error = "Không thể tạo báo cáo xe tháng." });
            }
        }

        [HttpGet("monthly-subscription-revenue")]
        public async Task<IActionResult> GetMonthlySubscriptionRevenueReport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            try
            {
                var report = await _reportExportService.GetMonthlyRevenueReportAsync(startDate, endDate);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating monthly subscription revenue report");
                return StatusCode(500, new { error = "Không thể tạo báo cáo doanh thu xe tháng." });
            }
        }

        [HttpGet("export/transactions/{format}")]
        public Task<IActionResult> ExportTransactions(
            string format,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? paymentMethod,
            [FromQuery] string? searchTerm,
            [FromQuery] string? status)
        {
            return Export(new ReportExportRequest
            {
                ReportType = "transactions",
                Format = format,
                StartDate = startDate,
                EndDate = endDate,
                PaymentMethod = paymentMethod,
                SearchTerm = searchTerm,
                Status = status
            });
        }

        [HttpGet("export/revenue/{format}")]
        public Task<IActionResult> ExportRevenue(string format, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            return Export(new ReportExportRequest
            {
                ReportType = "revenue",
                Format = format,
                StartDate = startDate,
                EndDate = endDate
            });
        }

        [HttpGet("export/monthly-subscriptions/{format}")]
        public Task<IActionResult> ExportMonthlySubscriptions(
            string format,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? paymentMethod,
            [FromQuery] string? vehicleType,
            [FromQuery] string? status,
            [FromQuery] string? searchTerm)
        {
            return Export(new ReportExportRequest
            {
                ReportType = "monthly-subscriptions",
                Format = format,
                StartDate = startDate,
                EndDate = endDate,
                PaymentMethod = paymentMethod,
                VehicleType = vehicleType,
                Status = status,
                SearchTerm = searchTerm
            });
        }

        [HttpGet("export/monthly-subscription-revenue/{format}")]
        public Task<IActionResult> ExportMonthlySubscriptionRevenue(string format, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            return Export(new ReportExportRequest
            {
                ReportType = "monthly-subscription-revenue",
                Format = format,
                StartDate = startDate,
                EndDate = endDate
            });
        }

        private async Task<IActionResult> Export(ReportExportRequest request)
        {
            try
            {
                var result = await _reportExportService.ExportAsync(request);
                Response.Headers.CacheControl = "no-store";
                return File(result.Content, result.ContentType, result.FileName);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report {ReportType} as {Format}", request.ReportType, request.Format);
                return StatusCode(500, new { error = "Không thể xuất báo cáo. Vui lòng thử lại." });
            }
        }
    }
}
