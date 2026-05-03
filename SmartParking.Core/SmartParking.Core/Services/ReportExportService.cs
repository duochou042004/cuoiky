using ClosedXML.Excel;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using MongoDB.Driver;
using SmartParking.Core.Data;
using SmartParking.Core.Models;
using System.Globalization;
using System.Text;

namespace SmartParking.Core.Services
{
    public class ReportExportService
    {
        private const int PdfMaxRows = 500;
        private readonly MongoDBContext _context;
        private readonly ILogger<ReportExportService> _logger;

        public ReportExportService(MongoDBContext context, ILogger<ReportExportService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<TransactionReportDto> GetTransactionReportAsync(DateTime? startDate, DateTime? endDate, string? paymentMethod, string? searchTerm = null, string? status = null)
        {
            var range = NormalizeRange(startDate, endDate);
            var builder = Builders<Transaction>.Filter;
            var filter = builder.Gte(t => t.Timestamp, range.Start) & builder.Lte(t => t.Timestamp, range.End);

            if (!string.IsNullOrWhiteSpace(paymentMethod) && !IsAll(paymentMethod))
            {
                filter &= builder.Eq(t => t.PaymentMethod, paymentMethod.Trim().ToUpperInvariant());
            }

            if (!string.IsNullOrWhiteSpace(status) && !IsAll(status))
            {
                filter &= builder.Eq(t => t.Status, status.Trim().ToUpperInvariant());
            }

            var transactions = await _context.Transactions
                .Find(filter)
                .SortByDescending(t => t.Timestamp)
                .ToListAsync();

            var rows = await EnrichTransactionsAsync(transactions);
            rows = ApplySearch(rows, searchTerm).ToList();

            return new TransactionReportDto
            {
                StartDate = range.Start,
                EndDate = range.End,
                Summary = BuildTransactionSummary(rows),
                Transactions = rows
            };
        }

        public async Task<RevenueReportDto> GetRevenueReportAsync(DateTime? startDate, DateTime? endDate)
        {
            var range = NormalizeRange(startDate, endDate);
            var filter = Builders<Transaction>.Filter.Gte(t => t.Timestamp, range.Start)
                & Builders<Transaction>.Filter.Lte(t => t.Timestamp, range.End)
                & Builders<Transaction>.Filter.Eq(t => t.Status, "COMPLETED");

            var transactions = await _context.Transactions.Find(filter).ToListAsync();
            var rows = await EnrichTransactionsAsync(transactions);
            var parkingRows = rows.Where(r => !IsMonthlyTransaction(r.Type)).ToList();

            return new RevenueReportDto
            {
                StartDate = range.Start,
                EndDate = range.End,
                Scope = "PARKING_FEE_ONLY",
                RevenueByPaymentMethod = BuildPaymentRevenue(parkingRows),
                RevenueByVehicleType = BuildVehicleRevenue(parkingRows),
                DailyRevenue = BuildDailyRevenue(parkingRows),
                TotalRevenue = parkingRows.Sum(r => r.Amount)
            };
        }

        public async Task<MonthlySubscriptionReportDto> GetMonthlySubscriptionReportAsync(DateTime? startDate, DateTime? endDate, string? paymentMethod = null, string? vehicleType = null, string? status = null, string? searchTerm = null)
        {
            var range = NormalizeRange(startDate, endDate);
            var builder = Builders<Transaction>.Filter;
            var filter = builder.Gte(t => t.Timestamp, range.Start)
                & builder.Lte(t => t.Timestamp, range.End)
                & builder.In(t => t.Type, new[] { "MONTHLY_SUBSCRIPTION", "MONTHLY_RENEWAL" });

            if (!string.IsNullOrWhiteSpace(status) && !IsAll(status))
            {
                filter &= builder.Eq(t => t.Status, status.Trim().ToUpperInvariant());
            }

            if (!string.IsNullOrWhiteSpace(paymentMethod) && !IsAll(paymentMethod))
            {
                filter &= builder.Eq(t => t.PaymentMethod, paymentMethod.Trim().ToUpperInvariant());
            }

            var transactions = await _context.Transactions
                .Find(filter)
                .SortByDescending(t => t.Timestamp)
                .ToListAsync();

            var rows = await EnrichTransactionsAsync(transactions);

            if (!string.IsNullOrWhiteSpace(vehicleType) && !IsAll(vehicleType))
            {
                var normalizedVehicleType = NormalizeVehicleType(vehicleType);
                rows = rows.Where(r => r.VehicleType == normalizedVehicleType).ToList();
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                rows = ApplySearch(rows, searchTerm).ToList();
            }

            var completed = rows.Where(r => r.Status == "COMPLETED").ToList();

            return new MonthlySubscriptionReportDto
            {
                StartDate = range.Start,
                EndDate = range.End,
                Summary = new MonthlySubscriptionSummaryDto
                {
                    TotalSubscriptions = rows.Count,
                    ValidSubscriptions = completed.Count,
                    ExpiredSubscriptions = rows.Count(r => r.Status == "FAILED" || r.Status == "TIMEOUT"),
                    CancelledSubscriptions = rows.Count(r => r.Status == "CANCELLED" || r.Status == "REFUNDED"),
                    TotalPackageAmount = completed.Sum(r => r.Amount),
                    ExpiringSoon = rows.Count(r => r.Status == "PENDING")
                },
                Transactions = rows
            };
        }

        public async Task<MonthlyRevenueReportDto> GetMonthlyRevenueReportAsync(DateTime? startDate, DateTime? endDate)
        {
            var range = NormalizeRange(startDate, endDate);
            var filter = Builders<Transaction>.Filter.Gte(t => t.Timestamp, range.Start)
                & Builders<Transaction>.Filter.Lte(t => t.Timestamp, range.End)
                & Builders<Transaction>.Filter.Eq(t => t.Status, "COMPLETED");

            var transactions = await _context.Transactions.Find(filter).ToListAsync();
            var rows = (await EnrichTransactionsAsync(transactions)).Where(r => IsMonthlyTransaction(r.Type)).ToList();

            return new MonthlyRevenueReportDto
            {
                StartDate = range.Start,
                EndDate = range.End,
                RevenueByPaymentMethod = BuildPaymentRevenue(rows),
                RevenueByVehicleType = BuildVehicleRevenue(rows),
                DailyRevenue = BuildDailyMonthlyRevenue(rows),
                TotalRevenue = rows.Sum(r => r.Amount),
                TotalTransactions = rows.Count,
                NewSubscriptions = rows.Count(r => string.Equals(r.Type, "MONTHLY_SUBSCRIPTION", StringComparison.OrdinalIgnoreCase)),
                Renewals = rows.Count(r => string.Equals(r.Type, "MONTHLY_RENEWAL", StringComparison.OrdinalIgnoreCase))
            };
        }

        public async Task<ReportExportResult> ExportAsync(ReportExportRequest request)
        {
            var reportType = request.ReportType.Trim().ToLowerInvariant();
            var format = request.Format.Trim().ToLowerInvariant();
            var range = NormalizeRange(request.StartDate, request.EndDate);

            var report = reportType switch
            {
                "transactions" => await BuildTransactionExportAsync(request),
                "revenue" => await BuildRevenueExportAsync(request),
                "monthly-subscriptions" => await BuildMonthlySubscriptionsExportAsync(request),
                "monthly-subscription-revenue" => await BuildMonthlyRevenueExportAsync(request),
                _ => throw new ArgumentException("Unsupported report type")
            };

            var extension = format == "excel" ? "xlsx" : format;
            var content = format switch
            {
                "csv" => BuildCsv(report.Columns, report.Rows),
                "excel" => BuildExcel(report),
                "pdf" => BuildPdf(report),
                _ => throw new ArgumentException("Unsupported export format")
            };

            return new ReportExportResult
            {
                Content = content,
                ContentType = format switch
                {
                    "csv" => "text/csv; charset=utf-8",
                    "excel" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    _ => "application/pdf"
                },
                FileName = $"smart-parking-{reportType}-{range.Start:yyyyMMdd}-{range.End:yyyyMMdd}-{DateTime.UtcNow:yyyyMMddHHmmss}.{extension}"
            };
        }

        private async Task<ReportDocument> BuildTransactionExportAsync(ReportExportRequest request)
        {
            var data = await GetTransactionReportAsync(request.StartDate, request.EndDate, request.PaymentMethod, request.SearchTerm, request.Status);
            return new ReportDocument
            {
                Title = "Báo cáo giao dịch bãi xe",
                Subtitle = BuildSubtitle(data.StartDate, data.EndDate, request.PaymentMethod, request.SearchTerm),
                Summary = new Dictionary<string, object?>
                {
                    ["Tổng giao dịch"] = data.Summary.TotalTransactions,
                    ["Hoàn thành"] = data.Summary.CompletedTransactions,
                    ["Đang xử lý"] = data.Summary.PendingTransactions,
                    ["Thất bại"] = data.Summary.FailedTransactions,
                    ["Tổng tiền đã thu"] = FormatCurrency(data.Summary.TotalAmount),
                    ["Giá trị trung bình"] = FormatCurrency(data.Summary.AverageAmount)
                },
                Columns = new[] { "Mã GD", "Ngày", "Biển số", "Loại xe", "Vị trí", "Thời lượng", "Mô tả", "Số tiền", "PTTT", "Trạng thái" },
                Rows = data.Transactions.Select(r => new object?[]
                {
                    r.TransactionId,
                    FormatDateTime(r.Timestamp),
                    r.LicensePlate,
                    DisplayVehicleType(r.VehicleType),
                    r.SlotId,
                    FormatDuration(r.DurationMinutes),
                    r.Description,
                    r.Amount,
                    r.PaymentMethod,
                    r.Status
                }).ToList()
            };
        }

        private async Task<ReportDocument> BuildRevenueExportAsync(ReportExportRequest request)
        {
            var data = await GetRevenueReportAsync(request.StartDate, request.EndDate);
            return new ReportDocument
            {
                Title = "Báo cáo doanh thu gửi xe vãng lai",
                Subtitle = BuildSubtitle(data.StartDate, data.EndDate, null, null),
                Summary = new Dictionary<string, object?>
                {
                    ["Tổng doanh thu"] = FormatCurrency(data.TotalRevenue),
                    ["Tiền mặt"] = FormatCurrency(data.RevenueByPaymentMethod.GetValueOrDefault("CASH")),
                    ["MoMo"] = FormatCurrency(data.RevenueByPaymentMethod.GetValueOrDefault("MOMO")),
                    ["Stripe"] = FormatCurrency(data.RevenueByPaymentMethod.GetValueOrDefault("STRIPE"))
                },
                Columns = new[] { "Ngày", "Tổng", "Tiền mặt", "MoMo", "Stripe" },
                Rows = data.DailyRevenue.Select(r => new object?[] { FormatDate(r.Date), r.Total, r.Cash, r.Momo, r.Stripe }).ToList()
            };
        }

        private async Task<ReportDocument> BuildMonthlySubscriptionsExportAsync(ReportExportRequest request)
        {
            var data = await GetMonthlySubscriptionReportAsync(request.StartDate, request.EndDate, request.PaymentMethod, request.VehicleType, request.Status, request.SearchTerm);
            return new ReportDocument
            {
                Title = "Báo cáo danh sách xe tháng",
                Subtitle = BuildSubtitle(data.StartDate, data.EndDate, request.VehicleType, request.SearchTerm),
                Summary = new Dictionary<string, object?>
                {
                    ["Tổng đăng ký"] = data.Summary.TotalSubscriptions,
                    ["Còn hiệu lực"] = data.Summary.ValidSubscriptions,
                    ["Sắp hết hạn"] = data.Summary.ExpiringSoon,
                    ["Hết hạn"] = data.Summary.ExpiredSubscriptions,
                    ["Tổng giá trị gói"] = FormatCurrency(data.Summary.TotalPackageAmount)
                },
                Columns = new[] { "Mã GD", "Ngày", "Mã xe", "Biển số", "Loại xe", "Loại gói", "Mô tả", "Số tiền", "PTTT", "Trạng thái" },
                Rows = data.Transactions.Select(r => new object?[]
                {
                    r.TransactionId,
                    FormatDateTime(r.Timestamp),
                    r.VehicleId,
                    r.LicensePlate,
                    DisplayVehicleType(r.VehicleType),
                    r.Type == "MONTHLY_RENEWAL" ? "Gia hạn" : "Đăng ký mới",
                    r.Description,
                    r.Amount,
                    r.PaymentMethod,
                    r.Status
                }).ToList()
            };
        }

        private async Task<ReportDocument> BuildMonthlyRevenueExportAsync(ReportExportRequest request)
        {
            var data = await GetMonthlyRevenueReportAsync(request.StartDate, request.EndDate);
            return new ReportDocument
            {
                Title = "Báo cáo doanh thu xe tháng",
                Subtitle = BuildSubtitle(data.StartDate, data.EndDate, null, null),
                Summary = new Dictionary<string, object?>
                {
                    ["Tổng doanh thu"] = FormatCurrency(data.TotalRevenue),
                    ["Tổng giao dịch"] = data.TotalTransactions,
                    ["Đăng ký mới"] = data.NewSubscriptions,
                    ["Gia hạn"] = data.Renewals
                },
                Columns = new[] { "Ngày", "Tổng", "Đăng ký mới", "Gia hạn", "Ô tô", "Xe máy", "Tiền mặt", "MoMo", "Stripe" },
                Rows = data.DailyRevenue.Select(r => new object?[] { FormatDate(r.Date), r.Total, r.NewSubscription, r.Renewal, r.Car, r.Motorcycle, r.Cash, r.Momo, r.Stripe }).ToList()
            };
        }

        private async Task<List<TransactionReportRowDto>> EnrichTransactionsAsync(List<Transaction> transactions)
        {
            var vehicleIds = transactions.Select(t => t.VehicleId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            var vehicles = vehicleIds.Count == 0
                ? new List<Vehicle>()
                : await _context.Vehicles.Find(v => vehicleIds.Contains(v.VehicleId)).ToListAsync();
            var vehicleMap = vehicles.GroupBy(v => v.VehicleId).ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.EntryTime).First());

            return transactions.Select(t =>
            {
                vehicleMap.TryGetValue(t.VehicleId, out var vehicle);
                var paymentTime = t.PaymentDetails?.PaymentTime ?? t.Timestamp;
                var durationMinutes = vehicle?.EntryTime == null ? null : (int?)Math.Max(0, (int)Math.Round(((vehicle.ExitTime ?? paymentTime) - vehicle.EntryTime).TotalMinutes));

                return new TransactionReportRowDto
                {
                    TransactionId = Safe(t.TransactionId),
                    Timestamp = t.Timestamp,
                    VehicleId = Safe(t.VehicleId),
                    LicensePlate = Safe(vehicle?.LicensePlate, Safe(t.VehicleId)),
                    VehicleType = NormalizeVehicleType(vehicle?.VehicleType ?? GuessVehicleType(t.VehicleId)),
                    SlotId = Safe(vehicle?.SlotId),
                    EntryTime = vehicle?.EntryTime,
                    ExitTime = vehicle?.ExitTime,
                    DurationMinutes = durationMinutes,
                    Amount = t.Amount,
                    Type = Safe(t.Type),
                    PaymentMethod = Safe(t.PaymentMethod),
                    Status = Safe(t.Status),
                    Description = Safe(t.Description),
                    PaymentReference = Safe(t.PaymentDetails?.TransactionReference ?? t.PaymentDetails?.MomoTransactionId ?? t.PaymentDetails?.StripePaymentIntentId)
                };
            }).ToList();
        }

        private static IEnumerable<TransactionReportRowDto> ApplySearch(IEnumerable<TransactionReportRowDto> rows, string? searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return rows;
            }

            var term = searchTerm.Trim().ToLowerInvariant();
            return rows.Where(r =>
                r.TransactionId.ToLowerInvariant().Contains(term) ||
                r.VehicleId.ToLowerInvariant().Contains(term) ||
                r.LicensePlate.ToLowerInvariant().Contains(term) ||
                r.Description.ToLowerInvariant().Contains(term) ||
                r.PaymentMethod.ToLowerInvariant().Contains(term) ||
                r.Status.ToLowerInvariant().Contains(term));
        }

        private static TransactionSummaryDto BuildTransactionSummary(IReadOnlyCollection<TransactionReportRowDto> rows)
        {
            var completed = rows.Where(r => r.Status == "COMPLETED").ToList();
            var totalAmount = completed.Sum(r => r.Amount);

            return new TransactionSummaryDto
            {
                TotalTransactions = rows.Count,
                CompletedTransactions = completed.Count,
                PendingTransactions = rows.Count(r => r.Status == "PENDING"),
                FailedTransactions = rows.Count(r => r.Status == "FAILED" || r.Status == "TIMEOUT"),
                TotalAmount = totalAmount,
                AverageAmount = completed.Count == 0 ? 0 : totalAmount / completed.Count,
                PaymentMethods = BuildPaymentRevenue(completed)
            };
        }

        private static Dictionary<string, decimal> BuildPaymentRevenue(IEnumerable<TransactionReportRowDto> rows)
        {
            var completed = rows.Where(r => r.Status == "COMPLETED").ToList();
            return new Dictionary<string, decimal>
            {
                ["CASH"] = completed.Where(r => r.PaymentMethod == "CASH").Sum(r => r.Amount),
                ["MOMO"] = completed.Where(r => r.PaymentMethod == "MOMO").Sum(r => r.Amount),
                ["STRIPE"] = completed.Where(r => r.PaymentMethod == "STRIPE").Sum(r => r.Amount),
                ["TOTAL"] = completed.Sum(r => r.Amount)
            };
        }

        private static Dictionary<string, decimal> BuildVehicleRevenue(IEnumerable<TransactionReportRowDto> rows)
        {
            var completed = rows.Where(r => r.Status == "COMPLETED").ToList();
            return new Dictionary<string, decimal>
            {
                ["CAR"] = completed.Where(r => r.VehicleType == "CAR").Sum(r => r.Amount),
                ["MOTORBIKE"] = completed.Where(r => r.VehicleType == "MOTORBIKE" || r.VehicleType == "MOTORCYCLE").Sum(r => r.Amount),
                ["OTHER"] = completed.Where(r => r.VehicleType != "CAR" && r.VehicleType != "MOTORBIKE" && r.VehicleType != "MOTORCYCLE").Sum(r => r.Amount),
                ["TOTAL"] = completed.Sum(r => r.Amount)
            };
        }

        private static List<DailyRevenueDto> BuildDailyRevenue(IEnumerable<TransactionReportRowDto> rows)
        {
            return rows.Where(r => r.Status == "COMPLETED")
                .GroupBy(r => r.Timestamp.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DailyRevenueDto
                {
                    Date = g.Key,
                    Total = g.Sum(r => r.Amount),
                    Cash = g.Where(r => r.PaymentMethod == "CASH").Sum(r => r.Amount),
                    Momo = g.Where(r => r.PaymentMethod == "MOMO").Sum(r => r.Amount),
                    Stripe = g.Where(r => r.PaymentMethod == "STRIPE").Sum(r => r.Amount)
                }).ToList();
        }

        private static List<DailyMonthlyRevenueDto> BuildDailyMonthlyRevenue(IEnumerable<TransactionReportRowDto> rows)
        {
            return rows.Where(r => r.Status == "COMPLETED")
                .GroupBy(r => r.Timestamp.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DailyMonthlyRevenueDto
                {
                    Date = g.Key,
                    Total = g.Sum(r => r.Amount),
                    NewSubscription = g.Where(r => r.Type == "MONTHLY_SUBSCRIPTION").Sum(r => r.Amount),
                    Renewal = g.Where(r => r.Type == "MONTHLY_RENEWAL").Sum(r => r.Amount),
                    Car = g.Where(r => r.VehicleType == "CAR").Sum(r => r.Amount),
                    Motorcycle = g.Where(r => r.VehicleType == "MOTORBIKE" || r.VehicleType == "MOTORCYCLE").Sum(r => r.Amount),
                    Cash = g.Where(r => r.PaymentMethod == "CASH").Sum(r => r.Amount),
                    Momo = g.Where(r => r.PaymentMethod == "MOMO").Sum(r => r.Amount),
                    Stripe = g.Where(r => r.PaymentMethod == "STRIPE").Sum(r => r.Amount)
                }).ToList();
        }

        private static byte[] BuildCsv(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(',', columns.Select(CsvEscape)));
            foreach (var row in rows)
            {
                builder.AppendLine(string.Join(',', row.Select(CsvEscape)));
            }

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
        }

        private static byte[] BuildExcel(ReportDocument report)
        {
            using var workbook = new XLWorkbook();
            workbook.Properties.Author = "Smart Parking System";
            workbook.Properties.Title = report.Title;
            workbook.Properties.Created = DateTime.UtcNow;

            var summary = workbook.Worksheets.Add("Tong quan");
            summary.Cell(1, 1).Value = report.Title;
            summary.Range(1, 1, 1, 4).Merge().Style.Font.SetBold().Font.SetFontSize(16);
            summary.Cell(2, 1).Value = report.Subtitle;
            summary.Range(2, 1, 2, 4).Merge();

            var row = 4;
            foreach (var item in report.Summary)
            {
                summary.Cell(row, 1).Value = item.Key;
                summary.Cell(row, 1).Style.Font.SetBold();
                SetCellValue(summary.Cell(row, 2), item.Value);
                row++;
            }

            summary.Columns().AdjustToContents();

            var details = workbook.Worksheets.Add("Chi tiet");
            for (var i = 0; i < report.Columns.Count; i++)
            {
                details.Cell(1, i + 1).Value = report.Columns[i];
            }

            var header = details.Range(1, 1, 1, report.Columns.Count);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("1E3A8A");
            header.Style.Font.FontColor = XLColor.White;

            for (var r = 0; r < report.Rows.Count; r++)
            {
                for (var c = 0; c < report.Columns.Count; c++)
                {
                    SetCellValue(details.Cell(r + 2, c + 1), report.Rows[r][c]);
                }
            }

            var usedRange = details.RangeUsed();
            if (usedRange != null)
            {
                usedRange.CreateTable();
            }

            details.SheetView.FreezeRows(1);
            details.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private byte[] BuildPdf(ReportDocument report)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, PageSize.A4.Rotate());

            var normalFont = CreateFont(false);
            var boldFont = CreateFont(true);

            document.Add(new Paragraph("SMART PARKING SYSTEM")
                .SetFont(boldFont)
                .SetFontSize(18)
                .SetTextAlignment(TextAlignment.CENTER));
            document.Add(new Paragraph(report.Title)
                .SetFont(boldFont)
                .SetFontSize(15)
                .SetTextAlignment(TextAlignment.CENTER));
            document.Add(new Paragraph(report.Subtitle)
                .SetFont(normalFont)
                .SetFontSize(10)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginBottom(12));

            var summaryTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1, 1, 1 })).UseAllAvailableWidth();
            foreach (var item in report.Summary)
            {
                summaryTable.AddCell(new Cell().Add(new Paragraph(item.Key).SetFont(boldFont).SetFontSize(9)));
                summaryTable.AddCell(new Cell().Add(new Paragraph(FormatPdfValue(item.Value)).SetFont(normalFont).SetFontSize(9)));
            }
            document.Add(summaryTable.SetMarginBottom(14));

            if (report.Rows.Count > PdfMaxRows)
            {
                document.Add(new Paragraph($"Bản PDF chỉ hiển thị {PdfMaxRows:N0} dòng đầu tiên trong tổng số {report.Rows.Count:N0} dòng. Vui lòng dùng Excel/CSV để lấy đầy đủ dữ liệu.")
                    .SetFont(normalFont)
                    .SetFontSize(9));
            }

            var weights = Enumerable.Repeat(1f, report.Columns.Count).ToArray();
            var table = new Table(UnitValue.CreatePercentArray(weights)).UseAllAvailableWidth();
            foreach (var column in report.Columns)
            {
                table.AddHeaderCell(new Cell().Add(new Paragraph(column).SetFont(boldFont).SetFontSize(7)));
            }

            foreach (var row in report.Rows.Take(PdfMaxRows))
            {
                foreach (var value in row)
                {
                    table.AddCell(new Cell().Add(new Paragraph(FormatPdfValue(value)).SetFont(normalFont).SetFontSize(7)));
                }
            }
            document.Add(table);

            document.Add(new Paragraph($"Xuất lúc: {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
                .SetFont(normalFont)
                .SetFontSize(8)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetMarginTop(12));

            document.Close();
            return stream.ToArray();
        }

        private PdfFont CreateFont(bool bold)
        {
            try
            {
                var fontPath = bold
                    ? "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
                    : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";

                if (File.Exists(fontPath))
                {
                    return PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load Unicode font for report PDF. Falling back to Helvetica.");
            }

            return PdfFontFactory.CreateFont(bold ? StandardFonts.HELVETICA_BOLD : StandardFonts.HELVETICA);
        }

        private static void SetCellValue(IXLCell cell, object? value)
        {
            switch (value)
            {
                case null:
                    cell.Value = string.Empty;
                    break;
                case DateTime date:
                    cell.Value = date;
                    cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";
                    break;
                case int number:
                    cell.Value = number;
                    break;
                case decimal amount:
                    cell.Value = amount;
                    cell.Style.NumberFormat.Format = "#,##0";
                    break;
                case double doubleValue:
                    cell.Value = doubleValue;
                    break;
                default:
                    cell.Value = SanitizeExcelText(value.ToString() ?? string.Empty);
                    break;
            }
        }

        private static string CsvEscape(object? value)
        {
            var text = FormatCsvValue(value);
            if (text.Length > 0 && "=+-@".Contains(text[0]))
            {
                text = "'" + text;
            }

            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        private static string FormatCsvValue(object? value)
        {
            return value switch
            {
                null => string.Empty,
                DateTime date => FormatDateTime(date),
                decimal amount => amount.ToString(CultureInfo.InvariantCulture),
                double number => number.ToString(CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static string SanitizeExcelText(string text)
        {
            return text.Length > 0 && "=+-@".Contains(text[0]) ? "'" + text : text;
        }

        private static string FormatPdfValue(object? value)
        {
            return value switch
            {
                null => string.Empty,
                DateTime date => FormatDateTime(date),
                decimal amount => FormatCurrency(amount),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static string BuildSubtitle(DateTime start, DateTime end, string? filter, string? searchTerm)
        {
            var pieces = new List<string> { $"Kỳ báo cáo: {start:dd/MM/yyyy HH:mm} - {end:dd/MM/yyyy HH:mm}" };
            if (!string.IsNullOrWhiteSpace(filter) && !IsAll(filter)) pieces.Add($"Bộ lọc: {filter}");
            if (!string.IsNullOrWhiteSpace(searchTerm)) pieces.Add($"Từ khóa: {searchTerm}");
            return string.Join(" | ", pieces);
        }

        private static ReportDateRange NormalizeRange(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate ?? DateTime.Today.AddDays(-30);
            var end = endDate ?? DateTime.Today.AddDays(1).AddTicks(-1);
            if (end.TimeOfDay == TimeSpan.Zero)
            {
                end = end.Date.AddDays(1).AddTicks(-1);
            }

            if (end < start)
            {
                (start, end) = (end, start);
            }

            return new ReportDateRange(start, end);
        }

        private static bool IsMonthlyTransaction(string? type)
        {
            return string.Equals(type, "MONTHLY_SUBSCRIPTION", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "MONTHLY_RENEWAL", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAll(string value) => string.Equals(value.Trim(), "ALL", StringComparison.OrdinalIgnoreCase);

        private static string Safe(string? value, string fallback = "") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string GuessVehicleType(string? vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId)) return "OTHER";
            return vehicleId.StartsWith("C", StringComparison.OrdinalIgnoreCase) || vehicleId.StartsWith("MC", StringComparison.OrdinalIgnoreCase)
                ? "CAR"
                : "MOTORBIKE";
        }

        private static string NormalizeVehicleType(string? vehicleType)
        {
            var value = Safe(vehicleType).ToUpperInvariant();
            return value switch
            {
                "MOTORCYCLE" => "MOTORBIKE",
                "MOTO" => "MOTORBIKE",
                "BIKE" => "MOTORBIKE",
                "CAR" => "CAR",
                _ => string.IsNullOrWhiteSpace(value) ? "OTHER" : value
            };
        }

        private static string DisplayVehicleType(string? vehicleType)
        {
            return NormalizeVehicleType(vehicleType) switch
            {
                "CAR" => "Ô tô",
                "MOTORBIKE" => "Xe máy",
                _ => "Khác"
            };
        }

        private static string FormatDate(DateTime date) => date.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN"));
        private static string FormatDateTime(DateTime date) => date.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo("vi-VN"));
        private static string FormatCurrency(decimal amount) => string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0:N0} VNĐ", amount);
        private static string FormatDuration(int? minutes)
        {
            if (minutes is null) return "N/A";
            var value = Math.Max(0, minutes.Value);
            return value < 60 ? $"{value} phút" : $"{value / 60} giờ {value % 60} phút";
        }

        private record ReportDateRange(DateTime Start, DateTime End);

        private class ReportDocument
        {
            public string Title { get; set; } = string.Empty;
            public string Subtitle { get; set; } = string.Empty;
            public Dictionary<string, object?> Summary { get; set; } = new();
            public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
            public List<object?[]> Rows { get; set; } = new();
        }
    }

    public class ReportExportRequest
    {
        public string ReportType { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? PaymentMethod { get; set; }
        public string? VehicleType { get; set; }
        public string? Status { get; set; }
        public string? SearchTerm { get; set; }
    }

    public class ReportExportResult
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    public class TransactionReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public TransactionSummaryDto Summary { get; set; } = new();
        public List<TransactionReportRowDto> Transactions { get; set; } = new();
    }

    public class TransactionSummaryDto
    {
        public int TotalTransactions { get; set; }
        public int CompletedTransactions { get; set; }
        public int PendingTransactions { get; set; }
        public int FailedTransactions { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AverageAmount { get; set; }
        public Dictionary<string, decimal> PaymentMethods { get; set; } = new();
    }

    public class TransactionReportRowDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string VehicleId { get; set; } = string.Empty;
        public string LicensePlate { get; set; } = string.Empty;
        public string VehicleType { get; set; } = string.Empty;
        public string SlotId { get; set; } = string.Empty;
        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public int? DurationMinutes { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PaymentReference { get; set; } = string.Empty;
    }

    public class RevenueReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Scope { get; set; } = string.Empty;
        public Dictionary<string, decimal> RevenueByPaymentMethod { get; set; } = new();
        public Dictionary<string, decimal> RevenueByVehicleType { get; set; } = new();
        public List<DailyRevenueDto> DailyRevenue { get; set; } = new();
        public decimal TotalRevenue { get; set; }
    }

    public class DailyRevenueDto
    {
        public DateTime Date { get; set; }
        public decimal Total { get; set; }
        public decimal Cash { get; set; }
        public decimal Momo { get; set; }
        public decimal Stripe { get; set; }
    }

    public class MonthlySubscriptionReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public MonthlySubscriptionSummaryDto Summary { get; set; } = new();
        public List<TransactionReportRowDto> Transactions { get; set; } = new();
    }

    public class MonthlySubscriptionSummaryDto
    {
        public int TotalSubscriptions { get; set; }
        public int ValidSubscriptions { get; set; }
        public int ExpiredSubscriptions { get; set; }
        public int CancelledSubscriptions { get; set; }
        public int ExpiringSoon { get; set; }
        public decimal TotalPackageAmount { get; set; }
    }

    public class MonthlySubscriptionRowDto
    {
        public string VehicleId { get; set; } = string.Empty;
        public string LicensePlate { get; set; } = string.Empty;
        public string VehicleType { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime RegistrationDate { get; set; }
        public DateTime? LastRenewalDate { get; set; }
        public int PackageDuration { get; set; }
        public decimal PackageAmount { get; set; }
        public int DiscountPercentage { get; set; }
        public string FixedSlotId { get; set; } = string.Empty;
        public int DaysRemaining { get; set; }
    }

    public class MonthlyRevenueReportDto : RevenueReportDto
    {
        public int TotalTransactions { get; set; }
        public int NewSubscriptions { get; set; }
        public int Renewals { get; set; }
        public new List<DailyMonthlyRevenueDto> DailyRevenue { get; set; } = new();
    }

    public class DailyMonthlyRevenueDto : DailyRevenueDto
    {
        public decimal NewSubscription { get; set; }
        public decimal Renewal { get; set; }
        public decimal Car { get; set; }
        public decimal Motorcycle { get; set; }
    }
}