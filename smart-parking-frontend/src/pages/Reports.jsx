import React, { useState, useEffect, useMemo } from 'react';
import { Card, Row, Col, Table, Button, Form, Spinner, Alert, Tabs, Tab, Badge, InputGroup, Dropdown } from 'react-bootstrap';
import DatePicker from 'react-datepicker';
import axios from 'axios';
import { FaFileInvoice, FaSearch, FaFilePdf, FaFileExcel, FaFileCsv, FaDownload, FaCalendarAlt, FaChartBar, FaHistory, FaFilter, FaCar, FaMotorcycle, FaMoneyBillWave, FaUserClock, FaPrint, FaCarAlt } from 'react-icons/fa';
import 'react-datepicker/dist/react-datepicker.css';
import { AreaChart, Area, BarChart, Bar, PieChart, Pie, Cell, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend } from 'recharts';
import { toast } from 'react-toastify';
import '../styles/reports-modern.css';

// Các màu cho biểu đồ
const COLORS = ['#0088FE', '#00C49F', '#FFBB28', '#FF8042', '#8884d8'];

const Reports = () => {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [startDate, setStartDate] = useState(new Date(new Date().setDate(new Date().getDate() - 30)));
  const [endDate, setEndDate] = useState(new Date());
  const [transactions, setTransactions] = useState([]);
  const [revenueData, setRevenueData] = useState(null);
  const [exportLoading, setExportLoading] = useState(false);
  const [activeTab, setActiveTab] = useState('transactions');
  const [monthlySubscriptions, setMonthlySubscriptions] = useState([]);
  const [monthlyRevenueData, setMonthlyRevenueData] = useState(null);
  const [filteredMonthlySubscriptions, setFilteredMonthlySubscriptions] = useState([]);
  const [monthlyChartData, setMonthlyChartData] = useState([]);
  const [generatingHistoricalData, setGeneratingHistoricalData] = useState(false);
  const [monthlySummaryData, setMonthlySummaryData] = useState({
    totalRevenue: 0,
    totalTransactions: 0,
    newSubscriptions: 0,
    renewals: 0,
    vehicleTypeDistribution: { CAR: 0, MOTORBIKE: 0 },
    paymentMethodDistribution: { CASH: 0, MOMO: 0, STRIPE: 0 }
  });
  const [paymentMethod, setPaymentMethod] = useState('ALL');
  const [searchTerm, setSearchTerm] = useState('');
  const [filteredTransactions, setFilteredTransactions] = useState([]);
  const [dateRangeText, setDateRangeText] = useState('Tháng này');
  const [chartData, setChartData] = useState([]);
  const [summaryData, setSummaryData] = useState({
    totalRevenue: 0,
    totalTransactions: 0,
    averageAmount: 0,
    vehicleTypeDistribution: { CAR: 0, MOTORBIKE: 0, OTHER: 0 },
    paymentMethodDistribution: { CASH: 0, MOMO: 0, STRIPE: 0, OTHER: 0 }
  });
  const [isDataLoaded, setIsDataLoaded] = useState(false);

  // Phân trang
  const [currentPage, setCurrentPage] = useState(1);
  const [itemsPerPage, setItemsPerPage] = useState(10);
  const [totalPages, setTotalPages] = useState(1);

  useEffect(() => {
    fetchData();
  }, []);

  useEffect(() => {
    if (transactions.length > 0) {
      filterTransactions();
      prepareSummaryData();
    }
  }, [searchTerm, transactions, paymentMethod]);

  useEffect(() => {
    if (monthlySubscriptions.length > 0) {
      filterMonthlySubscriptions();
      prepareMonthlySubscriptionSummaryData();
    }
  }, [searchTerm, monthlySubscriptions, paymentMethod]);

  // Tính toán các trang dữ liệu dựa trên phân trang
  const paginatedData = useMemo(() => {
    const startIndex = (currentPage - 1) * itemsPerPage;
    const endIndex = startIndex + itemsPerPage;
    return filteredTransactions.slice(startIndex, endIndex);
  }, [filteredTransactions, currentPage, itemsPerPage]);

  // Chuẩn bị dữ liệu tóm tắt khi transactions thay đổi
  const prepareSummaryData = () => {
    if (transactions.length === 0) return;

    let totalAmount = 0;
    let vehicleTypeDistribution = { CAR: 0, MOTORBIKE: 0, OTHER: 0 };
    let paymentMethodDistribution = { CASH: 0, MOMO: 0, STRIPE: 0, OTHER: 0 };

    // Lọc giao dịch theo phương thức thanh toán (nếu đã chọn)
    const filtered = paymentMethod === 'ALL'
      ? transactions
      : transactions.filter(t => t.paymentMethod === paymentMethod);

    filtered.forEach(transaction => {
      if (transaction.status !== 'COMPLETED') {
        return;
      }

      // Tính tổng doanh thu
      totalAmount += transaction.amount || 0;

      // Phân loại phương tiện
      if (transaction.vehicleType === 'CAR' || transaction.vehicleId?.startsWith('C')) {
        vehicleTypeDistribution.CAR++;
      } else if (['MOTORBIKE', 'MOTORCYCLE'].includes(transaction.vehicleType) || transaction.vehicleId?.startsWith('M')) {
        vehicleTypeDistribution.MOTORBIKE++;
      } else {
        vehicleTypeDistribution.OTHER++;
      }

      // Phân loại phương thức thanh toán
      if (transaction.paymentMethod === 'CASH') {
        paymentMethodDistribution.CASH++;
      } else if (transaction.paymentMethod === 'MOMO') {
        paymentMethodDistribution.MOMO++;
      } else if (transaction.paymentMethod === 'STRIPE') {
        paymentMethodDistribution.STRIPE++;
      } else {
        paymentMethodDistribution.OTHER++;
      }
    });

    setSummaryData({
      totalRevenue: totalAmount,
      totalTransactions: filtered.length,
      averageAmount: filtered.length > 0 ? totalAmount / filtered.length : 0,
      vehicleTypeDistribution,
      paymentMethodDistribution
    });
  };

  const filterTransactions = () => {
    if (!isDataLoaded) return;

    let filtered = [...transactions];

    // Lọc theo phương thức thanh toán
    if (paymentMethod !== 'ALL') {
      filtered = filtered.filter(t => t.paymentMethod === paymentMethod);
    }

    // Lọc theo từ khóa tìm kiếm
    if (searchTerm.trim()) {
      const term = searchTerm.toLowerCase();
      filtered = filtered.filter(transaction =>
        (transaction.vehicleId?.toLowerCase().includes(term)) ||
        (transaction.description?.toLowerCase().includes(term)) ||
        (transaction.paymentMethod?.toLowerCase().includes(term)) ||
        (transaction.licensePlate?.toLowerCase().includes(term))
      );
    }

    setFilteredTransactions(filtered);
    setTotalPages(Math.ceil(filtered.length / itemsPerPage));
    setCurrentPage(1); // Reset về trang đầu tiên khi lọc
  };

  const fetchData = async () => {
    try {
      setLoading(true);
      setError(null);

      const [transactions, revenueData, monthlySubscriptionsData, monthlyRevenueData] = await Promise.all([
        fetchTransactions(),
        fetchRevenueData(),
        fetchMonthlySubscriptions(),
        fetchMonthlySubscriptionRevenue()
      ]);

      // Chuẩn bị dữ liệu biểu đồ từ dữ liệu doanh thu
      if (revenueData && revenueData.dailyRevenue) {
        prepareChartData(revenueData.dailyRevenue);
      }

      // Chuẩn bị dữ liệu biểu đồ từ dữ liệu doanh thu gói tháng
      if (monthlyRevenueData && monthlyRevenueData.dailyRevenue) {
        prepareMonthlyChartData(monthlyRevenueData.dailyRevenue);
      }

      setIsDataLoaded(true);
      setLoading(false);
    } catch (err) {
      setError('Không thể tải dữ liệu. Vui lòng thử lại.');
      setLoading(false);
      console.error('Lỗi khi tải dữ liệu:', err);
      toast.error('Lỗi khi tải dữ liệu báo cáo');
    }
  };

  const prepareChartData = (dailyRevenue) => {
    // Format dữ liệu cho biểu đồ
    const formattedData = dailyRevenue.map(day => ({
      date: new Date(day.date).toLocaleDateString('vi-VN'),
      total: day.total,
      cash: day.cash || 0,
      momo: day.momo || 0,
      stripe: day.stripe || 0
    }));

    setChartData(formattedData);
  };

  const prepareMonthlyChartData = (dailyRevenue) => {
    // Format dữ liệu cho biểu đồ doanh thu gói tháng
    const formattedData = dailyRevenue.map(day => ({
      date: new Date(day.date).toLocaleDateString('vi-VN'),
      total: day.total,
      newSubscription: day.newSubscription || 0,
      renewal: day.renewal || 0,
      car: day.car || 0,
      motorcycle: day.motorcycle || 0,
      cash: day.cash || 0,
      momo: day.momo || 0,
      stripe: day.stripe || 0
    }));

    setMonthlyChartData(formattedData);
  };

  const filterMonthlySubscriptions = () => {
    if (!isDataLoaded) return;

    let filtered = [...monthlySubscriptions];

    // Lọc theo phương thức thanh toán
    if (paymentMethod !== 'ALL') {
      filtered = filtered.filter(t => t.paymentMethod === paymentMethod);
    }

    // Lọc theo từ khóa tìm kiếm
    if (searchTerm.trim()) {
      const term = searchTerm.toLowerCase();
      filtered = filtered.filter(transaction =>
        (transaction.vehicleId?.toLowerCase().includes(term)) ||
        (transaction.licensePlate?.toLowerCase().includes(term)) ||
        (transaction.description?.toLowerCase().includes(term)) ||
        (transaction.paymentMethod?.toLowerCase().includes(term)) ||
        (transaction.type?.toLowerCase().includes(term))
      );
    }

    setFilteredMonthlySubscriptions(filtered);
  };

  const prepareMonthlySubscriptionSummaryData = () => {
    if (monthlySubscriptions.length === 0) return;

    let totalAmount = 0;
    let newSubscriptions = 0;
    let renewals = 0;
    let vehicleTypeDistribution = { CAR: 0, MOTORBIKE: 0 };
    let paymentMethodDistribution = { CASH: 0, MOMO: 0, STRIPE: 0 };

    // Lọc giao dịch theo phương thức thanh toán (nếu đã chọn)
    const filtered = paymentMethod === 'ALL'
      ? monthlySubscriptions
      : monthlySubscriptions.filter(t => t.paymentMethod === paymentMethod);

    filtered.forEach(transaction => {
      if (transaction.status !== 'COMPLETED') {
        return;
      }

      // Tính tổng doanh thu
      totalAmount += transaction.amount || 0;

      // Phân loại loại giao dịch
      if (transaction.type === 'MONTHLY_SUBSCRIPTION') {
        newSubscriptions++;
      } else if (transaction.type === 'MONTHLY_RENEWAL') {
        renewals++;
      }

      // Phân loại phương tiện
      if (transaction.vehicleType === 'CAR' || transaction.vehicleId?.startsWith('C')) {
        vehicleTypeDistribution.CAR++;
      } else if (['MOTORBIKE', 'MOTORCYCLE'].includes(transaction.vehicleType) || transaction.vehicleId?.startsWith('M')) {
        vehicleTypeDistribution.MOTORBIKE++;
      }

      // Phân loại phương thức thanh toán
      if (transaction.paymentMethod === 'CASH') {
        paymentMethodDistribution.CASH++;
      } else if (transaction.paymentMethod === 'MOMO') {
        paymentMethodDistribution.MOMO++;
      } else if (transaction.paymentMethod === 'STRIPE') {
        paymentMethodDistribution.STRIPE++;
      }
    });

    setMonthlySummaryData({
      totalRevenue: totalAmount,
      totalTransactions: filtered.length,
      newSubscriptions,
      renewals,
      vehicleTypeDistribution,
      paymentMethodDistribution
    });
  };

  const fetchTransactions = async () => {
    try {
      const query = new URLSearchParams({
        startDate: startDate.toISOString(),
        endDate: endDate.toISOString(),
        paymentMethod,
        searchTerm: searchTerm.trim()
      });
      const response = await axios.get(`/api/reports/transactions?${query.toString()}`);
      const transactions = response.data.transactions || [];
      setTransactions(transactions);
      setFilteredTransactions(transactions);
      setTotalPages(Math.ceil(transactions.length / itemsPerPage));
      return response.data;
    } catch (err) {
      console.error('Lỗi khi tải giao dịch:', err);
      throw err;
    }
  };

  const fetchRevenueData = async () => {
    try {
      const response = await axios.get(`/api/reports/revenue?startDate=${startDate.toISOString()}&endDate=${endDate.toISOString()}`);
      setRevenueData(response.data);
      return response.data;
    } catch (err) {
      console.error('Lỗi khi tải dữ liệu doanh thu:', err);
      throw err;
    }
  };

  const fetchMonthlySubscriptions = async () => {
    try {
      const query = new URLSearchParams({
        startDate: startDate.toISOString(),
        endDate: endDate.toISOString(),
        paymentMethod,
        searchTerm: searchTerm.trim()
      });
      const response = await axios.get(`/api/reports/monthly-subscriptions?${query.toString()}`);
      setMonthlySubscriptions(response.data.transactions || []);
      setFilteredMonthlySubscriptions(response.data.transactions || []);
      return response.data;
    } catch (err) {
      console.error('Lỗi khi tải dữ liệu gói tháng:', err);
      throw err;
    }
  };

  const fetchMonthlySubscriptionRevenue = async () => {
    try {
      const response = await axios.get(`/api/reports/monthly-subscription-revenue?startDate=${startDate.toISOString()}&endDate=${endDate.toISOString()}`);
      setMonthlyRevenueData(response.data);
      return response.data;
    } catch (err) {
      console.error('Lỗi khi tải dữ liệu doanh thu gói tháng:', err);
      throw err;
    }
  };

  const handleSearch = () => {
    fetchData();
  };

  const handleDateRangeChange = (range) => {
    const today = new Date();
    let start, end;
    let rangeText = '';

    switch(range) {
      case 'today':
        start = new Date(today.setHours(0, 0, 0, 0));
        end = new Date();
        rangeText = 'Hôm nay';
        break;
      case 'week':
        start = new Date(today);
        start.setDate(today.getDate() - today.getDay());
        start.setHours(0, 0, 0, 0);
        end = new Date();
        rangeText = 'Tuần này';
        break;
      case 'month':
        start = new Date(today.getFullYear(), today.getMonth(), 1);
        end = new Date();
        rangeText = 'Tháng này';
        break;
      case 'quarter':
        const quarter = Math.floor(today.getMonth() / 3);
        start = new Date(today.getFullYear(), quarter * 3, 1);
        end = new Date();
        rangeText = 'Quý này';
        break;
      case 'year':
        start = new Date(today.getFullYear(), 0, 1);
        end = new Date();
        rangeText = 'Năm nay';
        break;
      default:
        start = new Date(today.setDate(today.getDate() - 30));
        end = new Date();
        rangeText = '30 ngày qua';
    }

    setStartDate(start);
    setEndDate(end);
    setDateRangeText(rangeText);

    // Lấy dữ liệu với khoảng thời gian mới
    setTimeout(() => fetchData(), 0);
  };

  const handlePaymentMethodChange = (method) => {
    setPaymentMethod(method);
    // Kích hoạt lọc lại dữ liệu
    setTimeout(() => {
      filterTransactions();
      filterMonthlySubscriptions();
    }, 0);
  };

  const handleExport = async (format) => {
    try {
      setExportLoading(true);

      const url = getExportUrl(format);
      const response = await axios.get(url, { responseType: 'blob' });
      const contentDisposition = response.headers['content-disposition'];
      const fileName = getExportFileName(format, contentDisposition);
      downloadBlob(response.data, fileName);

      toast.success(`Đã tải xuống báo cáo ${fileName}`);
    } catch (err) {
      setError(`Lỗi khi xuất file ${format.toUpperCase()}. Vui lòng thử lại.`);
      console.error(`Lỗi khi xuất ${format}:`, err);
      toast.error(`Xuất báo cáo thất bại: ${err.message}`);
    } finally {
      setExportLoading(false);
    }
  };

  const handlePrint = () => {
    const cleanup = () => {
      document.body.classList.remove('printing-report');
      window.removeEventListener('afterprint', cleanup);
    };

    document.body.classList.add('printing-report');
    window.addEventListener('afterprint', cleanup);
    setTimeout(() => {
      window.print();
      setTimeout(cleanup, 800);
    }, 120);
  };

  const getExportUrl = (format) => {
    const query = new URLSearchParams({
      startDate: startDate.toISOString(),
      endDate: endDate.toISOString()
    });

    if (searchTerm.trim()) {
      query.set('searchTerm', searchTerm.trim());
    }

    if (activeTab === 'transactions') {
      query.set('paymentMethod', paymentMethod);
      return `/api/reports/export/transactions/${format}?${query.toString()}`;
    }

    if (activeTab === 'revenue') {
      return `/api/reports/export/revenue/${format}?${query.toString()}`;
    }

    if (activeTab === 'monthly-subscriptions') {
      query.set('paymentMethod', paymentMethod);
      return `/api/reports/export/monthly-subscriptions/${format}?${query.toString()}`;
    }

    return `/api/reports/export/monthly-subscription-revenue/${format}?${query.toString()}`;
  };

  const getExportFileName = (format, contentDisposition) => {
    const match = contentDisposition?.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
    if (match?.[1]) {
      return decodeURIComponent(match[1].replace(/\"/g, ''));
    }

    const tabName = {
      transactions: 'giao-dich',
      revenue: 'doanh-thu',
      'monthly-subscriptions': 'xe-thang',
      'monthly-revenue': 'doanh-thu-xe-thang'
    }[activeTab] || 'bao-cao';

    const extension = format === 'excel' ? 'xlsx' : format;
    const stamp = new Date().toISOString().slice(0, 10);
    return `smart-parking-${tabName}-${stamp}.${extension}`;
  };

  const downloadBlob = (blob, fileName) => {
    const blobUrl = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = blobUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(blobUrl);
  };

  const handleGenerateHistoricalTransactions = async () => {
    try {
      setGeneratingHistoricalData(true);
      setError(null);

      const response = await axios.post('/api/monthlyvehicle/generate-historical-transactions');

      toast.success(`Đã tạo ${response.data.totalCreatedTransactions} giao dịch lịch sử cho xe tháng`);

      // Refresh data
      fetchData();
    } catch (err) {
      console.error('Error generating historical transactions:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi tạo dữ liệu lịch sử');
      toast.error('Lỗi khi tạo dữ liệu lịch sử');
    } finally {
      setGeneratingHistoricalData(false);
    }
  };

  const formatDate = (dateString) => {
    if (!dateString) return 'N/A';
    const date = new Date(dateString);
    return date.toLocaleString('vi-VN');
  };

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN').format(amount) + ' VNĐ';
  };

  const getPaymentMethodBadge = (method) => {
    switch (method) {
      case 'CASH':
        return <Badge bg="success">Tiền mặt</Badge>;
      case 'MOMO':
        return <Badge bg="danger">MoMo</Badge>;
      case 'STRIPE':
        return <Badge bg="primary">Stripe</Badge>;
      default:
        return <Badge bg="secondary">{method}</Badge>;
    }
  };

  const getStatusBadge = (status) => {
    switch (status) {
      case 'COMPLETED':
        return <Badge bg="success">Hoàn thành</Badge>;
      case 'PENDING':
        return <Badge bg="warning">Đang xử lý</Badge>;
      case 'FAILED':
        return <Badge bg="danger">Thất bại</Badge>;
      default:
        return <Badge bg="secondary">{status}</Badge>;
    }
  };

  // Chuẩn bị dữ liệu cho biểu đồ tròn
  const prepareDistributionChartData = (distribution) => {
    return Object.keys(distribution).map(key => ({
      name: key,
      value: distribution[key]
    })).filter(item => item.value > 0);
  };

  // Xử lý phân trang
  const handlePageChange = (page) => {
    setCurrentPage(page);
  };

  const renderPagination = () => {
    const pages = [];

    // Hiển thị tối đa 5 nút trang
    const startPage = Math.max(1, currentPage - 2);
    const endPage = Math.min(totalPages, startPage + 4);

    // Nút Previous
    pages.push(
      <Button
        key="prev"
        variant="outline-secondary"
        size="sm"
        className="me-1"
        disabled={currentPage === 1}
        onClick={() => handlePageChange(currentPage - 1)}
      >
        &laquo;
      </Button>
    );

    // Nút trang đầu tiên
    if (startPage > 1) {
      pages.push(
        <Button
          key="1"
          variant={currentPage === 1 ? "primary" : "outline-secondary"}
          size="sm"
          className="me-1"
          onClick={() => handlePageChange(1)}
        >
          1
        </Button>
      );

      // Hiển thị dấu ... nếu không liên tục với trang đầu tiên
      if (startPage > 2) {
        pages.push(<span key="ellipsis1" className="mx-1">...</span>);
      }
    }

    // Các nút trang chính
    for (let i = startPage; i <= endPage; i++) {
      pages.push(
        <Button
          key={i}
          variant={currentPage === i ? "primary" : "outline-secondary"}
          size="sm"
          className="me-1"
          onClick={() => handlePageChange(i)}
        >
          {i}
        </Button>
      );
    }

    // Hiển thị dấu ... nếu không liên tục với trang cuối
    if (endPage < totalPages - 1) {
      pages.push(<span key="ellipsis2" className="mx-1">...</span>);
    }

    // Nút trang cuối cùng nếu không phải là một trong các trang đã hiển thị
    if (endPage < totalPages) {
      pages.push(
        <Button
          key={totalPages}
          variant={currentPage === totalPages ? "primary" : "outline-secondary"}
          size="sm"
          className="me-1"
          onClick={() => handlePageChange(totalPages)}
        >
          {totalPages}
        </Button>
      );
    }

    // Nút Next
    pages.push(
      <Button
        key="next"
        variant="outline-secondary"
        size="sm"
        disabled={currentPage === totalPages}
        onClick={() => handlePageChange(currentPage + 1)}
      >
        &raquo;
      </Button>
    );

    return (
      <div className="d-flex justify-content-center mt-3">
        {pages}
      </div>
    );
  };

  return (
    <div className="report-page reports-2026">
      <div className="report-hero print-keep">
        <div>
          <Badge bg="primary" className="report-kicker">Smart Parking Intelligence</Badge>
          <h1>Báo cáo & Thống kê</h1>
          <p>Trung tâm phân tích doanh thu, giao dịch và xe tháng với trải nghiệm xuất file có xác thực.</p>
        </div>
        <div className="report-hero-actions">
          <Button variant="light" onClick={handlePrint} disabled={exportLoading}>
            <FaPrint className="me-2" /> In bản xem hiện tại
          </Button>
          <Dropdown>
            <Dropdown.Toggle variant="primary" disabled={exportLoading}>
              {exportLoading ? <Spinner size="sm" animation="border" className="me-2" /> : <FaDownload className="me-2" />}
              Xuất file
            </Dropdown.Toggle>
            <Dropdown.Menu align="end">
              <Dropdown.Item onClick={() => handleExport('excel')}><FaFileExcel className="me-2" /> Excel chuẩn bảng tính</Dropdown.Item>
              <Dropdown.Item onClick={() => handleExport('pdf')}><FaFilePdf className="me-2" /> PDF in ấn</Dropdown.Item>
              <Dropdown.Item onClick={() => handleExport('csv')}><FaFileCsv className="me-2" /> CSV dữ liệu thô</Dropdown.Item>
            </Dropdown.Menu>
          </Dropdown>
        </div>
      </div>

      <Tabs
        activeKey={activeTab}
        onSelect={(k) => setActiveTab(k)}
        className="mb-3"
      >
        <Tab eventKey="transactions" title={<span><FaHistory className="me-2" />Giao dịch</span>}>
          <Card className="mb-4">
            <Card.Header>
              <Row className="align-items-center">
                <Col>
                  <h5 className="mb-0">Lịch sử giao dịch</h5>
                  <p className="text-muted small mb-0">Thống kê tất cả giao dịch theo khoảng thời gian</p>
                </Col>
                <Col xs="auto">
                  <Button variant="outline-primary" onClick={handlePrint} className="me-2">
                    <FaPrint className="me-1" /> In báo cáo
                  </Button>
                  <Dropdown>
                    <Dropdown.Toggle variant="outline-secondary" id="dropdown-time-range">
                      <FaCalendarAlt className="me-2" /> {dateRangeText}
                    </Dropdown.Toggle>
                    <Dropdown.Menu>
                      <Dropdown.Item onClick={() => handleDateRangeChange('today')}>Hôm nay</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('week')}>Tuần này</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('month')}>Tháng này</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('quarter')}>Quý này</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('year')}>Năm nay</Dropdown.Item>
                    </Dropdown.Menu>
                  </Dropdown>
                </Col>
              </Row>
            </Card.Header>
          </Card>

          <Row className="mb-4 align-items-center">
            <Col md={6}>
              <InputGroup>
                <Form.Control
                  type="text"
                  placeholder="Tìm kiếm theo biển số, loại thanh toán..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                />
                <Button variant="primary" onClick={() => filterTransactions()}>
                  <FaSearch />
                </Button>
              </InputGroup>
            </Col>
            <Col md={3}>
              <Dropdown>
                <Dropdown.Toggle variant="outline-secondary" className="w-100">
                  <FaMoneyBillWave className="me-2" />
                  {paymentMethod === 'ALL' ? 'Tất cả phương thức' :
                   paymentMethod === 'CASH' ? 'Tiền mặt' :
                   paymentMethod === 'MOMO' ? 'MoMo' :
                   paymentMethod === 'STRIPE' ? 'Stripe' : paymentMethod}
                </Dropdown.Toggle>
                <Dropdown.Menu>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('ALL')}>Tất cả phương thức</Dropdown.Item>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('CASH')}>Tiền mặt</Dropdown.Item>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('MOMO')}>MoMo</Dropdown.Item>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('STRIPE')}>Stripe</Dropdown.Item>
                </Dropdown.Menu>
              </Dropdown>
            </Col>
            <Col md={3} className="text-end">
              <Dropdown>
                <Dropdown.Toggle variant="outline-success" className="w-100">
                  <FaDownload className="me-1" /> Xuất báo cáo
                </Dropdown.Toggle>
                <Dropdown.Menu>
                  <Dropdown.Item onClick={() => handleExport('excel')}>
                    <FaFileExcel className="me-2" /> Excel
                  </Dropdown.Item>
                  <Dropdown.Item onClick={() => handleExport('pdf')}>
                    <FaFilePdf className="me-2" /> PDF
                  </Dropdown.Item>
                  <Dropdown.Item onClick={() => handleExport('csv')}>
                    <FaFileCsv className="me-2" /> CSV
                  </Dropdown.Item>
                </Dropdown.Menu>
              </Dropdown>
            </Col>
          </Row>

          <Row className="mb-4">
            <Col md={12}>
              <Card className="mb-3">
                <Card.Body className="p-3">
                  <Row>
                    <Col md={4}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Từ ngày:</span>
                        </div>
                        <DatePicker
                          selected={startDate}
                          onChange={date => setStartDate(date)}
                          selectsStart
                          startDate={startDate}
                          endDate={endDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={4}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Đến ngày:</span>
                        </div>
                        <DatePicker
                          selected={endDate}
                          onChange={date => setEndDate(date)}
                          selectsEnd
                          startDate={startDate}
                          endDate={endDate}
                          minDate={startDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={4} className="d-flex align-items-center justify-content-end">
                      <Button variant="primary" onClick={handleSearch}>
                        <FaFilter className="me-2" /> Lọc dữ liệu
                      </Button>
                    </Col>
                  </Row>
                </Card.Body>
              </Card>
            </Col>
          </Row>

          {error && <Alert variant="danger">{error}</Alert>}

          <Row className="mb-4">
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaFileInvoice size={24} color="#0d6efd" />
                  </div>
                  <h5>Tổng số giao dịch</h5>
                  <h2 className="mb-0 fw-bold">{summaryData.totalTransactions}</h2>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaMoneyBillWave size={24} color="#198754" />
                  </div>
                  <h5>Tổng doanh thu</h5>
                  <h2 className="mb-0 fw-bold">{formatCurrency(summaryData.totalRevenue)}</h2>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaUserClock size={24} color="#dc3545" />
                  </div>
                  <h5>Trung bình/giao dịch</h5>
                  <h2 className="mb-0 fw-bold">{formatCurrency(summaryData.averageAmount)}</h2>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaCalendarAlt size={24} color="#6f42c1" />
                  </div>
                  <h5>Khoảng thời gian</h5>
                  <h6 className="mb-0">{dateRangeText}</h6>
                  <p className="text-muted small mb-0">{startDate.toLocaleDateString('vi-VN')} - {endDate.toLocaleDateString('vi-VN')}</p>
                </Card.Body>
              </Card>
            </Col>
          </Row>

          <Row className="mb-4">
            <Col md={6}>
              <Card>
                <Card.Header>
                  <h5 className="mb-0"><FaChartBar className="me-2" /> Phân loại phương tiện</h5>
                </Card.Header>
                <Card.Body>
                  {!loading && (
                    <div style={{ width: '100%', height: 300 }}>
                      <ResponsiveContainer width="100%" height="100%">
                        <PieChart>
                          <Pie
                            data={prepareDistributionChartData(summaryData.vehicleTypeDistribution)}
                            cx="50%"
                            cy="50%"
                            labelLine={false}
                            outerRadius={80}
                            fill="#8884d8"
                            dataKey="value"
                            nameKey="name"
                            label={({ name, percent }) => `${name}: ${(percent * 100).toFixed(0)}%`}
                          >
                            {prepareDistributionChartData(summaryData.vehicleTypeDistribution).map((entry, index) => (
                              <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                            ))}
                          </Pie>
                          <Legend />
                          <Tooltip formatter={(value) => [value, 'Số lượng']} />
                        </PieChart>
                      </ResponsiveContainer>
                    </div>
                  )}
                </Card.Body>
                <Card.Footer>
                  <div className="d-flex justify-content-around">
                    <div className="text-center">
                      <FaCar className="text-primary me-1" />
                      <span>Ô tô: {summaryData.vehicleTypeDistribution.CAR}</span>
                    </div>
                    <div className="text-center">
                      <FaMotorcycle className="text-success me-1" />
                      <span>Xe máy: {summaryData.vehicleTypeDistribution.MOTORBIKE}</span>
                    </div>
                  </div>
                </Card.Footer>
              </Card>
            </Col>
            <Col md={6}>
              <Card>
                <Card.Header>
                  <h5 className="mb-0"><FaMoneyBillWave className="me-2" /> Phân loại thanh toán</h5>
                </Card.Header>
                <Card.Body>
                  {!loading && (
                    <div style={{ width: '100%', height: 300 }}>
                      <ResponsiveContainer width="100%" height="100%">
                        <BarChart
                          data={prepareDistributionChartData(summaryData.paymentMethodDistribution)}
                          margin={{ top: 20, right: 30, left: 20, bottom: 5 }}
                        >
                          <CartesianGrid strokeDasharray="3 3" />
                          <XAxis dataKey="name" />
                          <YAxis />
                          <Tooltip formatter={(value) => [value, 'Số lượng']} />
                          <Legend />
                          <Bar dataKey="value" name="Số giao dịch">
                            {prepareDistributionChartData(summaryData.paymentMethodDistribution).map((entry, index) => (
                              <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                            ))}
                          </Bar>
                        </BarChart>
                      </ResponsiveContainer>
                    </div>
                  )}
                </Card.Body>
                <Card.Footer>
                  <div className="d-flex justify-content-around">
                    <div className="text-center">
                      <span className="badge bg-success me-1">CASH</span>
                      <span>{summaryData.paymentMethodDistribution.CASH}</span>
                    </div>
                    <div className="text-center">
                      <span className="badge bg-danger me-1">MOMO</span>
                      <span>{summaryData.paymentMethodDistribution.MOMO}</span>
                    </div>
                    <div className="text-center">
                      <span className="badge bg-primary me-1">STRIPE</span>
                      <span>{summaryData.paymentMethodDistribution.STRIPE}</span>
                    </div>
                  </div>
                </Card.Footer>
              </Card>
            </Col>
          </Row>

          {loading ? (
            <div className="text-center my-5">
              <Spinner animation="border" role="status">
                <span className="visually-hidden">Đang tải...</span>
              </Spinner>
            </div>
          ) : filteredTransactions.length === 0 ? (
            <Alert variant="info">Không tìm thấy giao dịch nào cho khoảng thời gian đã chọn.</Alert>
          ) : (
            <>
              <Card className="mb-3">
                <Card.Body>
                  <Table responsive striped hover>
                    <thead>
                      <tr>
                        <th>Biển số</th>
                        <th>Loại xe</th>
                        <th>Thời gian vào</th>
                        <th>Thời gian ra</th>
                        <th>Thời gian gửi</th>
                        <th>Phí gửi xe</th>
                        <th>Thanh toán</th>
                        <th>Trạng thái</th>
                      </tr>
                    </thead>
                    <tbody>
                      {paginatedData.map((transaction, index) => (
                        <tr key={index}>
                          <td>{transaction.vehicleId}</td>
                          <td>
                            {transaction.vehicleId?.startsWith('C') ? 'Xe ô tô' :
                             transaction.vehicleId?.startsWith('M') ? 'Xe máy' :
                             transaction.type === 'CAR' ? 'Xe ô tô' :
                             transaction.type === 'MOTORCYCLE' ? 'Xe máy' :
                             transaction.type || 'Không xác định'}
                          </td>
                          <td>{formatDate(transaction.entryTime || transaction.timestamp)}</td>
                          <td>{transaction.exitTime ? formatDate(transaction.exitTime) : '-'}</td>
                          <td>
                            {transaction.exitTime ?
                              `${Math.round((new Date(transaction.exitTime) - new Date(transaction.entryTime || transaction.timestamp)) / (1000 * 60 * 60))} giờ` :
                              '0 giờ'}
                          </td>
                          <td>{transaction.amount?.toLocaleString()} đ</td>
                          <td>{getPaymentMethodBadge(transaction.paymentMethod)}</td>
                          <td>{getStatusBadge(transaction.status)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </Card.Body>
                <Card.Footer>
                  <Row>
                    <Col md={6}>
                      <div className="d-flex align-items-center">
                        <span className="me-2">Phân loại xe:</span>
                        <Badge bg="primary" className="me-2">Xe máy: {filteredTransactions.filter(t => t.type === 'MOTORCYCLE' || t.vehicleId?.startsWith('M')).length}</Badge>
                        <Badge bg="success">Xe ô tô: {filteredTransactions.filter(t => t.type === 'CAR' || t.vehicleId?.startsWith('C')).length}</Badge>
                      </div>
                    </Col>
                    <Col md={6} className="text-end">
                      <span>Tổng số giao dịch: <strong>{filteredTransactions.length}</strong></span>
                      <span className="ms-3">Hoàn thành: <strong>{filteredTransactions.filter(t => t.status === 'COMPLETED').length}</strong></span>
                      <span className="ms-3">Đang xử lý: <strong>{filteredTransactions.filter(t => t.status === 'PENDING').length}</strong></span>
                    </Col>
                  </Row>
                </Card.Footer>
              </Card>

              {/* Hiển thị phân trang */}
              {renderPagination()}
            </>
          )}
        </Tab>

        <Tab eventKey="revenue" title={<span><FaChartBar className="me-2" />Doanh thu</span>}>
          <Row className="mb-4 align-items-center">
            <Col md={9}>
              <Card className="mb-3">
                <Card.Body className="p-3">
                  <Row>
                    <Col md={5}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Từ ngày:</span>
                        </div>
                        <DatePicker
                          selected={startDate}
                          onChange={date => setStartDate(date)}
                          selectsStart
                          startDate={startDate}
                          endDate={endDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={5}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Đến ngày:</span>
                        </div>
                        <DatePicker
                          selected={endDate}
                          onChange={date => setEndDate(date)}
                          selectsEnd
                          startDate={startDate}
                          endDate={endDate}
                          minDate={startDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={2} className="d-flex align-items-center justify-content-end">
                      <Button variant="primary" onClick={handleSearch}>
                        <FaFilter className="me-2" /> Lọc
                      </Button>
                    </Col>
                  </Row>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3} className="text-end">
              <div className="d-flex justify-content-end">
                <Dropdown>
                  <Dropdown.Toggle variant="outline-secondary" id="dropdown-time-range">
                    <FaCalendarAlt className="me-2" /> {dateRangeText}
                  </Dropdown.Toggle>
                  <Dropdown.Menu>
                    <Dropdown.Item onClick={() => handleDateRangeChange('today')}>Hôm nay</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('week')}>Tuần này</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('month')}>Tháng này</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('quarter')}>Quý này</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('year')}>Năm nay</Dropdown.Item>
                  </Dropdown.Menu>
                </Dropdown>
              </div>
            </Col>
          </Row>

          {loading ? (
            <div className="text-center my-5">
              <Spinner animation="border" role="status">
                <span className="visually-hidden">Đang tải...</span>
              </Spinner>
            </div>
          ) : revenueData ? (
            <>
              <Row className="mb-4">
                <Col md={4}>
                  <Card className="text-center revenue-summary-card">
                    <Card.Body>
                      <Card.Title>Tổng doanh thu</Card.Title>
                      <div className="revenue-amount">
                        <h2>{revenueData.totalRevenue.toLocaleString()} VND</h2>
                      </div>
                      <Card.Text>
                        {formatDate(revenueData.startDate).split(',')[0]} - {formatDate(revenueData.endDate).split(',')[0]}
                      </Card.Text>
                    </Card.Body>
                  </Card>
                </Col>
                <Col md={8}>
                  <Row>
                    <Col md={4}>
                      <Card className="text-center bg-success text-white revenue-method-card">
                        <Card.Body>
                          <Card.Title>Tiền mặt</Card.Title>
                          <h3>{revenueData.revenueByPaymentMethod.CASH.toLocaleString()} VND</h3>
                          <div className="revenue-percentage">
                            <span>{revenueData.totalRevenue > 0 ? ((revenueData.revenueByPaymentMethod.CASH / revenueData.totalRevenue) * 100).toFixed(1) : 0}%</span>
                            <div className="progress">
                              <div className="progress-bar bg-light" style={{ width: `${revenueData.totalRevenue > 0 ? ((revenueData.revenueByPaymentMethod.CASH / revenueData.totalRevenue) * 100) : 0}%` }}></div>
                            </div>
                          </div>
                        </Card.Body>
                      </Card>
                    </Col>
                    <Col md={4}>
                      <Card className="text-center bg-danger text-white revenue-method-card">
                        <Card.Body>
                          <Card.Title>MoMo</Card.Title>
                          <h3>{revenueData.revenueByPaymentMethod.MOMO.toLocaleString()} VND</h3>
                          <div className="revenue-percentage">
                            <span>{revenueData.totalRevenue > 0 ? ((revenueData.revenueByPaymentMethod.MOMO / revenueData.totalRevenue) * 100).toFixed(1) : 0}%</span>
                            <div className="progress">
                              <div className="progress-bar bg-light" style={{ width: `${revenueData.totalRevenue > 0 ? ((revenueData.revenueByPaymentMethod.MOMO / revenueData.totalRevenue) * 100) : 0}%` }}></div>
                            </div>
                          </div>
                        </Card.Body>
                      </Card>
                    </Col>
                    <Col md={4}>
                      <Card className="text-center bg-primary text-white revenue-method-card">
                        <Card.Body>
                          <Card.Title>Stripe</Card.Title>
                          <h3>{revenueData.revenueByPaymentMethod.STRIPE.toLocaleString()} VND</h3>
                          <div className="revenue-percentage">
                            <span>{revenueData.totalRevenue > 0 ? ((revenueData.revenueByPaymentMethod.STRIPE / revenueData.totalRevenue) * 100).toFixed(1) : 0}%</span>
                            <div className="progress">
                              <div className="progress-bar bg-light" style={{ width: `${revenueData.totalRevenue > 0 ? ((revenueData.revenueByPaymentMethod.STRIPE / revenueData.totalRevenue) * 100) : 0}%` }}></div>
                            </div>
                          </div>
                        </Card.Body>
                      </Card>
                    </Col>
                  </Row>
                </Col>
              </Row>

              <Card className="mb-4">
                <Card.Header className="d-flex justify-content-between align-items-center">
                  <h5 className="mb-0">Biểu đồ doanh thu theo ngày</h5>
                  <div>
                    <Button variant="outline-primary" onClick={handlePrint} className="btn-sm">
                      <FaPrint className="me-1" /> In báo cáo
                    </Button>
                  </div>
                </Card.Header>
                <Card.Body>
                  <div style={{ width: '100%', height: 400 }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <AreaChart
                        data={chartData}
                        margin={{ top: 10, right: 30, left: 0, bottom: 0 }}
                      >
                        <CartesianGrid strokeDasharray="3 3" />
                        <XAxis dataKey="date" />
                        <YAxis />
                        <Tooltip formatter={(value) => new Intl.NumberFormat('vi-VN').format(value) + ' VND'} />
                        <Legend />
                        <Area type="monotone" dataKey="total" name="Tổng" stroke="#8884d8" fill="#8884d8" fillOpacity={0.3} />
                        <Area type="monotone" dataKey="cash" name="Tiền mặt" stroke="#82ca9d" fill="#82ca9d" fillOpacity={0.3} />
                        <Area type="monotone" dataKey="momo" name="MoMo" stroke="#ff6b6b" fill="#ff6b6b" fillOpacity={0.3} />
                        <Area type="monotone" dataKey="stripe" name="Stripe" stroke="#4dabf7" fill="#4dabf7" fillOpacity={0.3} />
                      </AreaChart>
                    </ResponsiveContainer>
                  </div>
                </Card.Body>
              </Card>

              <Card>
                <Card.Header className="d-flex justify-content-between align-items-center">
                  <h5 className="mb-0">Báo cáo doanh thu theo ngày</h5>
                  <div>
                    <Button
                      variant="outline-primary"
                      className="me-2 btn-sm"
                      onClick={() => handleExport('pdf')}
                      disabled={exportLoading}
                    >
                      <FaFilePdf className="me-1" /> PDF
                    </Button>
                    <Button
                      variant="outline-success"
                      className="me-2 btn-sm"
                      onClick={() => handleExport('excel')}
                      disabled={exportLoading}
                    >
                      <FaFileExcel className="me-1" /> Excel
                    </Button>
                    <Button
                      variant="outline-secondary"
                      className="btn-sm"
                      onClick={() => handleExport('csv')}
                      disabled={exportLoading}
                    >
                      <FaFileCsv className="me-1" /> CSV
                    </Button>
                  </div>
                </Card.Header>
                <Card.Body>
                  <Table responsive striped bordered>
                    <thead>
                      <tr>
                        <th>Ngày</th>
                        <th>Tổng (VND)</th>
                        <th>Tiền mặt (VND)</th>
                        <th>MoMo (VND)</th>
                        <th>Stripe (VND)</th>
                      </tr>
                    </thead>
                    <tbody>
                      {revenueData.dailyRevenue.map((day, index) => (
                        <tr key={index}>
                          <td>{formatDate(day.date).split(',')[0]}</td>
                          <td>{day.total.toLocaleString()}</td>
                          <td>{day.cash.toLocaleString()}</td>
                          <td>{day.momo.toLocaleString()}</td>
                          <td>{day.stripe.toLocaleString()}</td>
                        </tr>
                      ))}
                    </tbody>
                    <tfoot>
                      <tr className="table-active fw-bold">
                        <td>Tổng cộng</td>
                        <td>{revenueData.totalRevenue.toLocaleString()}</td>
                        <td>{revenueData.revenueByPaymentMethod.CASH.toLocaleString()}</td>
                        <td>{revenueData.revenueByPaymentMethod.MOMO.toLocaleString()}</td>
                        <td>{revenueData.revenueByPaymentMethod.STRIPE.toLocaleString()}</td>
                      </tr>
                    </tfoot>
                  </Table>
                </Card.Body>
              </Card>
            </>
          ) : (
            <Alert variant="info">Không có dữ liệu doanh thu cho khoảng thời gian đã chọn.</Alert>
          )}
        </Tab>

        <Tab eventKey="monthly-subscriptions" title={<span><FaCarAlt className="me-2" />Gói tháng</span>}>
          <Card className="mb-4">
            <Card.Header>
              <Row className="align-items-center">
                <Col>
                  <h5 className="mb-0">Giao dịch gói tháng</h5>
                  <p className="text-muted small mb-0">Thống kê giao dịch đăng ký và gia hạn gói tháng</p>
                </Col>
                <Col xs="auto">
                  <Button
                    variant="outline-warning"
                    onClick={handleGenerateHistoricalTransactions}
                    className="me-2"
                    disabled={generatingHistoricalData}
                  >
                    {generatingHistoricalData ? (
                      <>
                        <Spinner
                          as="span"
                          animation="border"
                          size="sm"
                          role="status"
                          aria-hidden="true"
                          className="me-1"
                        />
                        Đang xử lý...
                      </>
                    ) : (
                      <>
                        <FaHistory className="me-1" /> Tạo dữ liệu lịch sử
                      </>
                    )}
                  </Button>
                  <Button variant="outline-primary" onClick={handlePrint} className="me-2">
                    <FaPrint className="me-1" /> In báo cáo
                  </Button>
                  <Dropdown>
                    <Dropdown.Toggle variant="outline-secondary" id="dropdown-time-range">
                      <FaCalendarAlt className="me-2" /> {dateRangeText}
                    </Dropdown.Toggle>
                    <Dropdown.Menu>
                      <Dropdown.Item onClick={() => handleDateRangeChange('today')}>Hôm nay</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('week')}>Tuần này</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('month')}>Tháng này</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('quarter')}>Quý này</Dropdown.Item>
                      <Dropdown.Item onClick={() => handleDateRangeChange('year')}>Năm nay</Dropdown.Item>
                    </Dropdown.Menu>
                  </Dropdown>
                </Col>
              </Row>
            </Card.Header>
          </Card>

          <Row className="mb-4 align-items-center">
            <Col md={6}>
              <InputGroup>
                <Form.Control
                  type="text"
                  placeholder="Tìm kiếm theo biển số, loại thanh toán..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                />
                <Button variant="primary" onClick={() => filterMonthlySubscriptions()}>
                  <FaSearch />
                </Button>
              </InputGroup>
            </Col>
            <Col md={3}>
              <Dropdown>
                <Dropdown.Toggle variant="outline-secondary" className="w-100">
                  <FaMoneyBillWave className="me-2" />
                  {paymentMethod === 'ALL' ? 'Tất cả phương thức' :
                   paymentMethod === 'CASH' ? 'Tiền mặt' :
                   paymentMethod === 'MOMO' ? 'MoMo' :
                   paymentMethod === 'STRIPE' ? 'Stripe' : paymentMethod}
                </Dropdown.Toggle>
                <Dropdown.Menu>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('ALL')}>Tất cả phương thức</Dropdown.Item>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('CASH')}>Tiền mặt</Dropdown.Item>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('MOMO')}>MoMo</Dropdown.Item>
                  <Dropdown.Item onClick={() => handlePaymentMethodChange('STRIPE')}>Stripe</Dropdown.Item>
                </Dropdown.Menu>
              </Dropdown>
            </Col>
            <Col md={3} className="text-end">
              <Dropdown>
                <Dropdown.Toggle variant="outline-success" className="w-100">
                  <FaDownload className="me-1" /> Xuất báo cáo
                </Dropdown.Toggle>
                <Dropdown.Menu>
                  <Dropdown.Item onClick={() => handleExport('excel')}>
                    <FaFileExcel className="me-2" /> Excel
                  </Dropdown.Item>
                  <Dropdown.Item onClick={() => handleExport('pdf')}>
                    <FaFilePdf className="me-2" /> PDF
                  </Dropdown.Item>
                  <Dropdown.Item onClick={() => handleExport('csv')}>
                    <FaFileCsv className="me-2" /> CSV
                  </Dropdown.Item>
                </Dropdown.Menu>
              </Dropdown>
            </Col>
          </Row>

          <Row className="mb-4">
            <Col md={12}>
              <Card className="mb-3">
                <Card.Body className="p-3">
                  <Row>
                    <Col md={4}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Từ ngày:</span>
                        </div>
                        <DatePicker
                          selected={startDate}
                          onChange={date => setStartDate(date)}
                          selectsStart
                          startDate={startDate}
                          endDate={endDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={4}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Đến ngày:</span>
                        </div>
                        <DatePicker
                          selected={endDate}
                          onChange={date => setEndDate(date)}
                          selectsEnd
                          startDate={startDate}
                          endDate={endDate}
                          minDate={startDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={4} className="d-flex align-items-center justify-content-end">
                      <Button variant="primary" onClick={handleSearch}>
                        <FaFilter className="me-2" /> Lọc dữ liệu
                      </Button>
                    </Col>
                  </Row>
                </Card.Body>
              </Card>
            </Col>
          </Row>

          {error && <Alert variant="danger">{error}</Alert>}

          <Row className="mb-4">
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaFileInvoice size={24} color="#0d6efd" />
                  </div>
                  <h5>Tổng số giao dịch</h5>
                  <h2 className="mb-0 fw-bold">{monthlySummaryData.totalTransactions}</h2>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaMoneyBillWave size={24} color="#198754" />
                  </div>
                  <h5>Tổng doanh thu</h5>
                  <h2 className="mb-0 fw-bold">{formatCurrency(monthlySummaryData.totalRevenue)}</h2>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaUserClock size={24} color="#dc3545" />
                  </div>
                  <h5>Đăng ký mới</h5>
                  <h2 className="mb-0 fw-bold">{monthlySummaryData.newSubscriptions}</h2>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3}>
              <Card className="dashboard-stat-card">
                <Card.Body className="text-center">
                  <div className="stat-icon">
                    <FaCalendarAlt size={24} color="#6f42c1" />
                  </div>
                  <h5>Gia hạn</h5>
                  <h2 className="mb-0 fw-bold">{monthlySummaryData.renewals}</h2>
                </Card.Body>
              </Card>
            </Col>
          </Row>

          {loading ? (
            <div className="text-center my-5">
              <Spinner animation="border" role="status">
                <span className="visually-hidden">Đang tải...</span>
              </Spinner>
            </div>
          ) : filteredMonthlySubscriptions.length === 0 ? (
            <Alert variant="info">Không tìm thấy giao dịch gói tháng nào cho khoảng thời gian đã chọn.</Alert>
          ) : (
            <>
              <Card className="mb-3">
                <Card.Body>
                  <Table responsive striped hover>
                    <thead>
                      <tr>
                        <th>Mã giao dịch</th>
                        <th>Thời gian</th>
                        <th>Biển số</th>
                        <th>Loại xe</th>
                        <th>Loại gói</th>
                        <th>Số tiền</th>
                        <th>Thanh toán</th>
                        <th>Trạng thái</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredMonthlySubscriptions.map((transaction, index) => (
                        <tr key={index}>
                          <td>{transaction.transactionId}</td>
                          <td>{formatDate(transaction.timestamp)}</td>
                          <td>{transaction.licensePlate || transaction.vehicleId}</td>
                          <td>
                            {transaction.vehicleType === 'CAR' || transaction.vehicleId?.startsWith('C') ? 'Xe ô tô' :
                             ['MOTORBIKE', 'MOTORCYCLE'].includes(transaction.vehicleType) || transaction.vehicleId?.startsWith('M') ? 'Xe máy' : 'Không xác định'}
                          </td>
                          <td>
                            {transaction.type === 'MONTHLY_SUBSCRIPTION' ? 'Đăng ký mới' :
                             transaction.type === 'MONTHLY_RENEWAL' ? 'Gia hạn' : transaction.type}
                          </td>
                          <td>{transaction.amount?.toLocaleString()} đ</td>
                          <td>{getPaymentMethodBadge(transaction.paymentMethod)}</td>
                          <td>{getStatusBadge(transaction.status)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </Card.Body>
                <Card.Footer>
                  <Row>
                    <Col md={6}>
                      <div className="d-flex align-items-center">
                        <span className="me-2">Phân loại xe:</span>
                        <Badge bg="primary" className="me-2">Xe máy: {filteredMonthlySubscriptions.filter(t => t.vehicleId?.startsWith('M')).length}</Badge>
                        <Badge bg="success">Xe ô tô: {filteredMonthlySubscriptions.filter(t => t.vehicleId?.startsWith('C')).length}</Badge>
                      </div>
                    </Col>
                    <Col md={6} className="text-end">
                      <span>Tổng số giao dịch: <strong>{filteredMonthlySubscriptions.length}</strong></span>
                      <span className="ms-3">Hoàn thành: <strong>{filteredMonthlySubscriptions.filter(t => t.status === 'COMPLETED').length}</strong></span>
                      <span className="ms-3">Đang xử lý: <strong>{filteredMonthlySubscriptions.filter(t => t.status === 'PENDING').length}</strong></span>
                    </Col>
                  </Row>
                </Card.Footer>
              </Card>
            </>
          )}
        </Tab>

        <Tab eventKey="monthly-revenue" title={<span><FaChartBar className="me-2" />Doanh thu gói tháng</span>}>
          <Row className="mb-4 align-items-center">
            <Col md={9}>
              <Card className="mb-3">
                <Card.Body className="p-3">
                  <Row>
                    <Col md={5}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Từ ngày:</span>
                        </div>
                        <DatePicker
                          selected={startDate}
                          onChange={date => setStartDate(date)}
                          selectsStart
                          startDate={startDate}
                          endDate={endDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={5}>
                      <div className="d-flex align-items-center">
                        <div className="me-3">
                          <span className="text-muted">Đến ngày:</span>
                        </div>
                        <DatePicker
                          selected={endDate}
                          onChange={date => setEndDate(date)}
                          selectsEnd
                          startDate={startDate}
                          endDate={endDate}
                          minDate={startDate}
                          className="form-control"
                          dateFormat="dd/MM/yyyy"
                        />
                      </div>
                    </Col>
                    <Col md={2} className="d-flex align-items-center justify-content-end">
                      <Button variant="primary" onClick={handleSearch}>
                        <FaFilter className="me-2" /> Lọc
                      </Button>
                    </Col>
                  </Row>
                </Card.Body>
              </Card>
            </Col>
            <Col md={3} className="text-end">
              <div className="d-flex justify-content-end">
                <Dropdown>
                  <Dropdown.Toggle variant="outline-secondary" id="dropdown-time-range">
                    <FaCalendarAlt className="me-2" /> {dateRangeText}
                  </Dropdown.Toggle>
                  <Dropdown.Menu>
                    <Dropdown.Item onClick={() => handleDateRangeChange('today')}>Hôm nay</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('week')}>Tuần này</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('month')}>Tháng này</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('quarter')}>Quý này</Dropdown.Item>
                    <Dropdown.Item onClick={() => handleDateRangeChange('year')}>Năm nay</Dropdown.Item>
                  </Dropdown.Menu>
                </Dropdown>
              </div>
            </Col>
          </Row>

          {loading ? (
            <div className="text-center my-5">
              <Spinner animation="border" role="status">
                <span className="visually-hidden">Đang tải...</span>
              </Spinner>
            </div>
          ) : monthlyRevenueData ? (
            <>
              <Row className="mb-4">
                <Col md={4}>
                  <Card className="text-center revenue-summary-card">
                    <Card.Body>
                      <Card.Title>Tổng doanh thu gói tháng</Card.Title>
                      <div className="revenue-amount">
                        <h2>{monthlyRevenueData.totalRevenue.toLocaleString()} VND</h2>
                      </div>
                      <Card.Text>
                        {formatDate(monthlyRevenueData.startDate).split(',')[0]} - {formatDate(monthlyRevenueData.endDate).split(',')[0]}
                      </Card.Text>
                    </Card.Body>
                  </Card>
                </Col>
                <Col md={8}>
                  <Row>
                    <Col md={6}>
                      <Card className="text-center bg-primary text-white revenue-method-card">
                        <Card.Body>
                          <Card.Title>Đăng ký mới</Card.Title>
                          <h3>{monthlyRevenueData.revenueByCategory.NEW_SUBSCRIPTION.toLocaleString()} VND</h3>
                          <div className="revenue-percentage">
                            <span>{monthlyRevenueData.totalRevenue > 0 ? ((monthlyRevenueData.revenueByCategory.NEW_SUBSCRIPTION / monthlyRevenueData.totalRevenue) * 100).toFixed(1) : 0}%</span>
                            <div className="progress">
                              <div className="progress-bar bg-light" style={{ width: `${monthlyRevenueData.totalRevenue > 0 ? ((monthlyRevenueData.revenueByCategory.NEW_SUBSCRIPTION / monthlyRevenueData.totalRevenue) * 100) : 0}%` }}></div>
                            </div>
                          </div>
                        </Card.Body>
                      </Card>
                    </Col>
                    <Col md={6}>
                      <Card className="text-center bg-success text-white revenue-method-card">
                        <Card.Body>
                          <Card.Title>Gia hạn</Card.Title>
                          <h3>{monthlyRevenueData.revenueByCategory.RENEWAL.toLocaleString()} VND</h3>
                          <div className="revenue-percentage">
                            <span>{monthlyRevenueData.totalRevenue > 0 ? ((monthlyRevenueData.revenueByCategory.RENEWAL / monthlyRevenueData.totalRevenue) * 100).toFixed(1) : 0}%</span>
                            <div className="progress">
                              <div className="progress-bar bg-light" style={{ width: `${monthlyRevenueData.totalRevenue > 0 ? ((monthlyRevenueData.revenueByCategory.RENEWAL / monthlyRevenueData.totalRevenue) * 100) : 0}%` }}></div>
                            </div>
                          </div>
                        </Card.Body>
                      </Card>
                    </Col>
                  </Row>
                </Col>
              </Row>

              <Card className="mb-4">
                <Card.Header className="d-flex justify-content-between align-items-center">
                  <h5 className="mb-0">Biểu đồ doanh thu gói tháng theo ngày</h5>
                  <div>
                    <Button variant="outline-primary" onClick={handlePrint} className="btn-sm">
                      <FaPrint className="me-1" /> In báo cáo
                    </Button>
                  </div>
                </Card.Header>
                <Card.Body>
                  <div style={{ width: '100%', height: 400 }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <AreaChart
                        data={monthlyChartData}
                        margin={{ top: 10, right: 30, left: 0, bottom: 0 }}
                      >
                        <CartesianGrid strokeDasharray="3 3" />
                        <XAxis dataKey="date" />
                        <YAxis />
                        <Tooltip formatter={(value) => new Intl.NumberFormat('vi-VN').format(value) + ' VND'} />
                        <Legend />
                        <Area type="monotone" dataKey="total" name="Tổng" stroke="#8884d8" fill="#8884d8" fillOpacity={0.3} />
                        <Area type="monotone" dataKey="newSubscription" name="Đăng ký mới" stroke="#82ca9d" fill="#82ca9d" fillOpacity={0.3} />
                        <Area type="monotone" dataKey="renewal" name="Gia hạn" stroke="#ff6b6b" fill="#ff6b6b" fillOpacity={0.3} />
                      </AreaChart>
                    </ResponsiveContainer>
                  </div>
                </Card.Body>
              </Card>

              <Row className="mb-4">
                <Col md={6}>
                  <Card>
                    <Card.Header>
                      <h5 className="mb-0"><FaChartBar className="me-2" /> Phân loại phương tiện</h5>
                    </Card.Header>
                    <Card.Body>
                      <div style={{ width: '100%', height: 300 }}>
                        <ResponsiveContainer width="100%" height="100%">
                          <PieChart>
                            <Pie
                              data={[
                                { name: 'Xe ô tô', value: monthlyRevenueData.revenueByCategory.CAR },
                                { name: 'Xe máy', value: monthlyRevenueData.revenueByCategory.MOTORCYCLE }
                              ]}
                              cx="50%"
                              cy="50%"
                              labelLine={false}
                              outerRadius={80}
                              fill="#8884d8"
                              dataKey="value"
                              nameKey="name"
                              label={({ name, percent }) => `${name}: ${(percent * 100).toFixed(0)}%`}
                            >
                              <Cell key="cell-0" fill={COLORS[0]} />
                              <Cell key="cell-1" fill={COLORS[1]} />
                            </Pie>
                            <Legend />
                            <Tooltip formatter={(value) => value.toLocaleString() + ' VND'} />
                          </PieChart>
                        </ResponsiveContainer>
                      </div>
                    </Card.Body>
                    <Card.Footer>
                      <div className="d-flex justify-content-around">
                        <div className="text-center">
                          <FaCar className="text-primary me-1" />
                          <span>Ô tô: {monthlyRevenueData.revenueByCategory.CAR.toLocaleString()} VND</span>
                        </div>
                        <div className="text-center">
                          <FaMotorcycle className="text-success me-1" />
                          <span>Xe máy: {monthlyRevenueData.revenueByCategory.MOTORCYCLE.toLocaleString()} VND</span>
                        </div>
                      </div>
                    </Card.Footer>
                  </Card>
                </Col>
                <Col md={6}>
                  <Card>
                    <Card.Header>
                      <h5 className="mb-0"><FaMoneyBillWave className="me-2" /> Phân loại thanh toán</h5>
                    </Card.Header>
                    <Card.Body>
                      <div style={{ width: '100%', height: 300 }}>
                        <ResponsiveContainer width="100%" height="100%">
                          <BarChart
                            data={[
                              { name: 'Tiền mặt', value: monthlyRevenueData.revenueByCategory.CASH },
                              { name: 'MoMo', value: monthlyRevenueData.revenueByCategory.MOMO },
                              { name: 'Stripe', value: monthlyRevenueData.revenueByCategory.STRIPE }
                            ]}
                            margin={{ top: 20, right: 30, left: 20, bottom: 5 }}
                          >
                            <CartesianGrid strokeDasharray="3 3" />
                            <XAxis dataKey="name" />
                            <YAxis />
                            <Tooltip formatter={(value) => value.toLocaleString() + ' VND'} />
                            <Legend />
                            <Bar dataKey="value" name="Doanh thu">
                              <Cell key="cell-0" fill={COLORS[0]} />
                              <Cell key="cell-1" fill={COLORS[1]} />
                              <Cell key="cell-2" fill={COLORS[2]} />
                            </Bar>
                          </BarChart>
                        </ResponsiveContainer>
                      </div>
                    </Card.Body>
                    <Card.Footer>
                      <div className="d-flex justify-content-around">
                        <div className="text-center">
                          <span className="badge bg-success me-1">Tiền mặt</span>
                          <span>{monthlyRevenueData.revenueByCategory.CASH.toLocaleString()} VND</span>
                        </div>
                        <div className="text-center">
                          <span className="badge bg-danger me-1">MoMo</span>
                          <span>{monthlyRevenueData.revenueByCategory.MOMO.toLocaleString()} VND</span>
                        </div>
                        <div className="text-center">
                          <span className="badge bg-primary me-1">Stripe</span>
                          <span>{monthlyRevenueData.revenueByCategory.STRIPE.toLocaleString()} VND</span>
                        </div>
                      </div>
                    </Card.Footer>
                  </Card>
                </Col>
              </Row>

              <Card>
                <Card.Header className="d-flex justify-content-between align-items-center">
                  <h5 className="mb-0">Báo cáo doanh thu gói tháng theo ngày</h5>
                  <div>
                    <Button
                      variant="outline-primary"
                      className="me-2 btn-sm"
                      onClick={() => handleExport('pdf')}
                      disabled={exportLoading}
                    >
                      <FaFilePdf className="me-1" /> PDF
                    </Button>
                    <Button
                      variant="outline-success"
                      className="me-2 btn-sm"
                      onClick={() => handleExport('excel')}
                      disabled={exportLoading}
                    >
                      <FaFileExcel className="me-1" /> Excel
                    </Button>
                    <Button
                      variant="outline-secondary"
                      className="btn-sm"
                      onClick={() => handleExport('csv')}
                      disabled={exportLoading}
                    >
                      <FaFileCsv className="me-1" /> CSV
                    </Button>
                  </div>
                </Card.Header>
                <Card.Body>
                  <Table responsive striped bordered>
                    <thead>
                      <tr>
                        <th>Ngày</th>
                        <th>Tổng (VND)</th>
                        <th>Đăng ký mới (VND)</th>
                        <th>Gia hạn (VND)</th>
                        <th>Xe ô tô (VND)</th>
                        <th>Xe máy (VND)</th>
                      </tr>
                    </thead>
                    <tbody>
                      {monthlyChartData.map((day, index) => (
                        <tr key={index}>
                          <td>{day.date}</td>
                          <td>{day.total.toLocaleString()}</td>
                          <td>{day.newSubscription.toLocaleString()}</td>
                          <td>{day.renewal.toLocaleString()}</td>
                          <td>{day.car.toLocaleString()}</td>
                          <td>{day.motorcycle.toLocaleString()}</td>
                        </tr>
                      ))}
                    </tbody>
                    <tfoot>
                      <tr className="table-active fw-bold">
                        <td>Tổng cộng</td>
                        <td>{monthlyRevenueData.totalRevenue.toLocaleString()}</td>
                        <td>{monthlyRevenueData.revenueByCategory.NEW_SUBSCRIPTION.toLocaleString()}</td>
                        <td>{monthlyRevenueData.revenueByCategory.RENEWAL.toLocaleString()}</td>
                        <td>{monthlyRevenueData.revenueByCategory.CAR.toLocaleString()}</td>
                        <td>{monthlyRevenueData.revenueByCategory.MOTORCYCLE.toLocaleString()}</td>
                      </tr>
                    </tfoot>
                  </Table>
                </Card.Body>
              </Card>
            </>
          ) : (
            <Alert variant="info">Không có dữ liệu doanh thu gói tháng cho khoảng thời gian đã chọn.</Alert>
          )}
        </Tab>
      </Tabs>

      {/* CSS cho trang báo cáo */}
      <style type="text/css">
        {`
        .dashboard-stat-card {
          transition: all 0.3s;
          border-radius: 10px;
          box-shadow: 0 4px 6px rgba(0,0,0,0.1);
          height: 100%;
        }

        .dashboard-stat-card:hover {
          transform: translateY(-5px);
          box-shadow: 0 6px 12px rgba(0,0,0,0.15);
        }

        .stat-icon {
          background-color: rgba(13, 110, 253, 0.1);
          width: 50px;
          height: 50px;
          border-radius: 50%;
          display: flex;
          align-items: center;
          justify-content: center;
          margin: 0 auto 15px;
        }

        .revenue-summary-card {
          background: linear-gradient(120deg, #6a11cb 0%, #2575fc 100%);
          color: white;
          border-radius: 10px;
          box-shadow: 0 4px 15px rgba(0,0,0,0.15);
        }

        .revenue-amount {
          padding: 20px 0;
        }

        .revenue-method-card {
          border-radius: 10px;
          box-shadow: 0 4px 6px rgba(0,0,0,0.1);
          transition: all 0.3s;
        }

        .revenue-method-card:hover {
          transform: translateY(-5px);
        }

        .revenue-percentage {
          margin-top: 10px;
        }

        .revenue-percentage .progress {
          height: 6px;
          margin-top: 5px;
          background-color: rgba(255,255,255,0.2);
        }

        @media print {
          .nav-tabs, .btn, .dropdown, .form-control, .input-group,
          .card-header button, .pagination {
            display: none !important;
          }

          .card {
            box-shadow: none !important;
            border: 1px solid #ddd !important;
          }

          .container, .container-fluid {
            width: 100% !important;
            padding: 0 !important;
            margin: 0 !important;
          }

          h1 {
            font-size: 24pt !important;
            text-align: center !important;
            margin: 20pt 0 !important;
          }

          .card-body {
            padding: 10pt !important;
          }
        }
        `}
      </style>
    </div>
  );
};

export default Reports;
