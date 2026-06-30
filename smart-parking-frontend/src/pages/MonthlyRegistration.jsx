import React, { useState, useEffect } from 'react';
import { Container, Row, Col, Card, Form, Button, Alert, Spinner, Table, Badge, Tabs, Tab, Modal } from 'react-bootstrap';
import { FaPlus, FaEdit, FaTrash, FaFileInvoice, FaSearch, FaFilter, FaDownload, FaCalendarAlt, FaUser, FaCar } from 'react-icons/fa';
import DatePicker from 'react-datepicker';
import axios from 'axios';
import 'react-datepicker/dist/react-datepicker.css';
import { toast } from 'react-toastify';
import PaymentModal from '../components/PaymentModal';
import VehicleImageUpload from '../components/VehicleImageUpload';

const MonthlyRegistration = () => {
  // State for tabs
  const [activeTab, setActiveTab] = useState('registration');

  // State for registration form
  const [formData, setFormData] = useState({
    licensePlate: '',
    vehicleType: 'MOTORBIKE',
    customerName: '',
    customerPhone: '',
    customerEmail: '',
    packageDuration: 1
  });

  // State for package pricing
  const [packagePrice, setPackagePrice] = useState({
    price: 0,
    discountPercentage: 0,
    basePrice: 0,
    totalBeforeDiscount: 0
  });

  // State for discount tiers
  const [discountTiers, setDiscountTiers] = useState([]);

  // State for registered vehicles
  const [vehicles, setVehicles] = useState([]);
  const [filteredVehicles, setFilteredVehicles] = useState([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('ALL');

  // State for loading and errors
  const [loading, setLoading] = useState(false);
  const [vehiclesLoading, setVehiclesLoading] = useState(true);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);

  // State for payment
  const [showPaymentModal, setShowPaymentModal] = useState(false);
  const [selectedVehicle, setSelectedVehicle] = useState(null);
  const [paymentType, setPaymentType] = useState('');

  // State for renewal modal
  const [showRenewalModal, setShowRenewalModal] = useState(false);
  const [renewalData, setRenewalData] = useState({
    vehicleId: '',
    packageDuration: 1
  });
  const [renewalPrice, setRenewalPrice] = useState({
    price: 0,
    discountPercentage: 0,
    basePrice: 0,
    totalBeforeDiscount: 0
  });

  // Fetch registered vehicles and discount tiers on component mount
  useEffect(() => {
    fetchVehicles();
    fetchDiscountTiers();
  }, []);

  // Filter vehicles when search term or status filter changes
  useEffect(() => {
    filterVehicles();
  }, [searchTerm, statusFilter, vehicles]);

  // Calculate package price when form data changes
  useEffect(() => {
    calculatePackagePrice();
  }, [formData.vehicleType, formData.packageDuration]);

  // Fetch discount tiers from the backend
  const fetchDiscountTiers = async () => {
    try {
      const response = await axios.get('/api/settings/discounts', {
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        }
      });

      if (response.data && response.data.discountTiers) {
        setDiscountTiers(response.data.discountTiers);
      }
    } catch (err) {
      console.error('Error fetching discount tiers:', err);
    }
  };

  // Calculate renewal price when renewal data changes
  useEffect(() => {
    if (renewalData.vehicleId) {
      calculateRenewalPrice();
    }
  }, [renewalData.packageDuration, renewalData.vehicleId]);

  // Fetch registered vehicles
  const fetchVehicles = async () => {
    try {
      setVehiclesLoading(true);

      // Use the correct API endpoint
      const response = await axios.get('/api/monthlyvehicle', {
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        }
      });

      console.log('Monthly vehicles API response:', response.data);

      if (Array.isArray(response.data)) {
        setVehicles(response.data);
        setFilteredVehicles(response.data);
        console.log('Vehicles set to state:', response.data.length, 'vehicles');
      } else {
        console.error('API response is not an array:', response.data);
        setVehicles([]);
        setFilteredVehicles([]);
        toast.error('Failed to load monthly vehicles: Invalid data format');
      }

      setVehiclesLoading(false);
    } catch (err) {
      console.error('Error fetching vehicles:', err);
      toast.error('Failed to load monthly vehicles');
      setVehicles([]);
      setFilteredVehicles([]);
      setVehiclesLoading(false);
    }
  };

  // Filter vehicles based on search term and status filter
  const filterVehicles = () => {
    let filtered = vehicles;

    // Apply status filter
    if (statusFilter !== 'ALL') {
      filtered = filtered.filter(vehicle => vehicle.status === statusFilter);
    }

    // Apply search term
    if (searchTerm) {
      const term = searchTerm.toLowerCase();
      filtered = filtered.filter(vehicle =>
        vehicle.licensePlate.toLowerCase().includes(term) ||
        vehicle.customerName.toLowerCase().includes(term) ||
        vehicle.vehicleId.toLowerCase().includes(term)
      );
    }

    setFilteredVehicles(filtered);
  };

  // Calculate package price
  const calculatePackagePrice = async () => {
    try {
      const response = await axios.get(`/api/monthlyvehicle/calculate-price?vehicleType=${formData.vehicleType}&durationMonths=${formData.packageDuration}`, {
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        }
      });
      setPackagePrice(response.data);
    } catch (err) {
      console.error('Error calculating package price:', err);
    }
  };

  // Calculate renewal price
  const calculateRenewalPrice = async () => {
    try {
      const vehicle = vehicles.find(v => v.id === renewalData.vehicleId);
      if (vehicle) {
        const response = await axios.get(`/api/monthlyvehicle/calculate-price?vehicleType=${vehicle.vehicleType}&durationMonths=${renewalData.packageDuration}`, {
          headers: {
            'Accept': 'application/json',
            'Content-Type': 'application/json'
          }
        });
        setRenewalPrice(response.data);
      }
    } catch (err) {
      console.error('Error calculating renewal price:', err);
    }
  };

  // Handle form input changes
  const handleInputChange = (e) => {
    const { name, value } = e.target;
    setFormData({
      ...formData,
      [name]: value
    });
  };

  // Handle renewal form input changes
  const handleRenewalInputChange = (e) => {
    const { name, value } = e.target;
    setRenewalData({
      ...renewalData,
      [name]: value
    });
  };

  // Handle form submission
  const handleSubmit = async (e) => {
    e.preventDefault();

    try {
      setLoading(true);
      setError(null);
      setSuccess(null);

      // Validate form data
      if (!formData.licensePlate || !formData.customerName || !formData.customerPhone || !formData.customerEmail) {
        setError('Vui lòng điền đầy đủ thông tin');
        setLoading(false);
        return;
      }

      // Submit form data
      const response = await axios.post('/api/monthlyvehicle/register', formData, {
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        }
      });

      // Check if payment is required
      if (response.data.requiresPayment) {
        // Store registration data for payment
        setSelectedVehicle({
          ...response.data.paymentDetails,
          registrationId: response.data.registrationId,
          packageAmount: response.data.paymentAmount
        });

        // Set payment type to registration
        setPaymentType('registration');

        // Show payment modal
        setShowPaymentModal(true);

        setLoading(false);
      } else {
        // Reset form
        setFormData({
          licensePlate: '',
          vehicleType: 'MOTORBIKE',
          customerName: '',
          customerPhone: '',
          customerEmail: '',
          packageDuration: 1
        });

        // Show success message
        setSuccess('Đăng ký xe thành công');

        // Refresh vehicles list
        fetchVehicles();

        setLoading(false);
      }
    } catch (err) {
      console.error('Error registering vehicle:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi đăng ký xe');
      setLoading(false);
    }
  };

  // Handle renewal submission
  const handleRenewal = async () => {
    try {
      setLoading(true);
      setError(null);

      // Submit renewal data
      const response = await axios.post(`/api/monthlyvehicle/renew/${renewalData.vehicleId}`, {
        packageDuration: renewalData.packageDuration
      }, {
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        }
      });

      // Check if payment is required
      if (response.data.requiresPayment) {
        // Close renewal modal
        setShowRenewalModal(false);

        // Store renewal data for payment
        setSelectedVehicle({
          ...response.data.paymentDetails,
          renewalId: response.data.renewalId,
          packageAmount: response.data.paymentAmount
        });

        // Set payment type to renewal
        setPaymentType('renewal');

        // Show payment modal
        setShowPaymentModal(true);
      } else {
        // Close modal
        setShowRenewalModal(false);

        // Reset renewal data
        setRenewalData({
          vehicleId: '',
          packageDuration: 1
        });

        // Show success message
        setSuccess('Gia hạn xe thành công');

        // Refresh vehicles list
        fetchVehicles();
      }

      setLoading(false);
    } catch (err) {
      console.error('Error renewing vehicle:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi gia hạn xe');
      setLoading(false);
    }
  };

  // Handle cancellation
  const handleCancel = async (id) => {
    if (!window.confirm('Bạn có chắc chắn muốn hủy đăng ký xe này?')) {
      return;
    }

    try {
      setLoading(true);
      setError(null);

      // Submit cancellation
      const response = await axios.post(`/api/monthlyvehicle/cancel/${id}`, {}, {
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        }
      });

      // Show success message
      setSuccess('Hủy đăng ký xe thành công');

      // Refresh vehicles list
      fetchVehicles();

      setLoading(false);
    } catch (err) {
      console.error('Error cancelling vehicle:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi hủy đăng ký xe');
      setLoading(false);
    }
  };

  // Open renewal modal
  const openRenewalModal = (vehicle) => {
    setRenewalData({
      vehicleId: vehicle.id,
      packageDuration: 1
    });
    setShowRenewalModal(true);
  };

  // Open payment modal
  const openPaymentModal = (vehicle, type) => {
    setSelectedVehicle(vehicle);
    setPaymentType(type);
    setShowPaymentModal(true);
  };

  // Format date
  const formatDate = (dateString) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('vi-VN');
  };

  // Calculate days left
  const calculateDaysLeft = (endDate) => {
    const end = new Date(endDate);
    const now = new Date();
    const diffTime = end - now;
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
    return diffDays > 0 ? diffDays : 0;
  };

  // Get status badge
  const getStatusBadge = (status) => {
    switch (status) {
      case 'VALID':
        return <Badge bg="success">Còn hạn</Badge>;
      case 'EXPIRED':
        return <Badge bg="danger">Hết hạn</Badge>;
      case 'CANCELLED':
        return <Badge bg="secondary">Đã hủy</Badge>;
      default:
        return <Badge bg="info">{status}</Badge>;
    }
  };

  // Get vehicle type badge
  const getVehicleTypeBadge = (type) => {
    switch (type) {
      case 'CAR':
        return <Badge bg="primary">Xe ô tô</Badge>;
      case 'MOTORBIKE':
        return <Badge bg="info">Xe máy</Badge>;
      default:
        return <Badge bg="secondary">{type}</Badge>;
    }
  };

  // Handle recognition completion from VehicleImageUpload component
  const handleRecognitionComplete = (data) => {
    console.log("Recognition data received in parent:", data);

    // Update form data with recognized license plate and vehicle type
    setFormData(prev => ({
      ...prev,
      licensePlate: data.licensePlate,
      vehicleType: vehicleType
    }));

    // Also recalculate package price when vehicle type changes
    setTimeout(() => {
      calculatePackagePrice();
    }, 100);

    // Show a toast notification
    toast.success(`Nhận diện thành công: ${data.licensePlate} (${data.vehicleType === 'CAR' ? 'Ô tô' : 'Xe máy'})`);
  };

  return (
    <Container fluid className="py-4">
      <h2 className="mb-4">Đăng ký xe tháng</h2>
      <p className="text-muted">Quản lý đăng ký xe tháng và xem danh sách xe đăng ký</p>

      <Tabs
        activeKey={activeTab}
        onSelect={(k) => setActiveTab(k)}
        className="mb-4"
      >
        <Tab eventKey="registration" title="Đăng ký mới">
          <Row>
            <Col md={8}>
              <Card className="shadow-sm border-0">
                <Card.Header className="bg-primary text-white">
                  <div className="d-flex align-items-center">
                    <FaFileInvoice className="me-2" size={22} />
                    <div>
                      <h5 className="mb-0">Đăng ký xe tháng</h5>
                      <p className="text-white-50 small mb-0">Làm theo các bước dưới đây để hoàn tất đăng ký xe tháng</p>
                    </div>
                  </div>
                </Card.Header>
                <Card.Body>
                  <div className="mb-4">
                    <ol className="registration-steps">
                      <li className="step active"><span className="step-number">1</span> Nhập thông tin khách hàng</li>
                      <li className="step"><span className="step-number">2</span> Nhập thông tin xe</li>
                      <li className="step"><span className="step-number">3</span> Chọn gói đăng ký</li>
                      <li className="step"><span className="step-number">4</span> Thanh toán</li>
                    </ol>
                  </div>
                  {error && <Alert variant="danger">{error}</Alert>}
                  {success && <Alert variant="success">{success}</Alert>}

                  <Form onSubmit={handleSubmit} className="animated-form">
                    <h5 className="mb-3 mt-2"><FaUser className="me-2 text-primary" />Thông tin khách hàng</h5>
                    <Row>
                      <Col md={6}>
                        <Form.Group className="mb-3">
                          <Form.Label>Họ và tên <span className="text-danger">*</span></Form.Label>
                          <Form.Control
                            type="text"
                            name="customerName"
                            value={formData.customerName}
                            onChange={handleInputChange}
                            required
                            placeholder="Nhập họ và tên khách hàng"
                          />
                        </Form.Group>
                      </Col>
                      <Col md={6}>
                        <Form.Group className="mb-3">
                          <Form.Label>Số điện thoại <span className="text-danger">*</span></Form.Label>
                          <Form.Control
                            type="text"
                            name="customerPhone"
                            value={formData.customerPhone}
                            onChange={handleInputChange}
                            required
                            placeholder="Nhập số điện thoại"
                          />
                        </Form.Group>
                      </Col>
                    </Row>
                    <Row>
                      <Col md={12}>
                        <Form.Group className="mb-3">
                          <Form.Label>Email <span className="text-danger">*</span></Form.Label>
                          <Form.Control
                            type="email"
                            name="customerEmail"
                            value={formData.customerEmail}
                            onChange={handleInputChange}
                            required
                            placeholder="Nhập địa chỉ email khách hàng"
                          />
                        </Form.Group>
                      </Col>
                    </Row>

                    <h5 className="mb-3 mt-4"><FaCar className="me-2 text-primary" />Thông tin xe</h5>

                    {/* Vehicle Image Upload Component */}
                    <VehicleImageUpload onRecognitionComplete={handleRecognitionComplete} />

                    <Row>
                      <Col md={6}>
                        <Form.Group className="mb-3">
                          <Form.Label>Biển số xe <span className="text-danger">*</span></Form.Label>
                          <Form.Control
                            type="text"
                            name="licensePlate"
                            value={formData.licensePlate}
                            onChange={handleInputChange}
                            placeholder="Nhập biển số xe"
                            required
                          />
                          <Form.Text className="text-muted">
                            Biển số xe sẽ được tự động điền nếu nhận diện thành công từ ảnh
                          </Form.Text>
                        </Form.Group>

                        <Form.Group className="mb-3">
                          <Form.Label>Loại xe <span className="text-danger">*</span></Form.Label>
                          <Form.Select
                            name="vehicleType"
                            value={formData.vehicleType}
                            onChange={handleInputChange}
                            required
                          >
                            <option value="MOTORBIKE">Xe máy</option>
                            <option value="CAR">Ô tô</option>
                          </Form.Select>
                          <Form.Text className="text-muted">
                            Loại xe đã được tự động điền từ kết quả nhận diện ảnh (CAR = Ô tô, MOTORBIKE = Xe máy)
                          </Form.Text>
                        </Form.Group>
                      </Col>
                      <Col md={6}>
                        <Form.Group className="mb-3">
                          <Form.Label>Thời hạn đăng ký <span className="text-danger">*</span></Form.Label>
                          <Form.Select
                            name="packageDuration"
                            value={formData.packageDuration}
                            onChange={handleInputChange}
                            required
                          >
                            {/* Generate options based on discount tiers */}
                            {discountTiers.length > 0 ? (
                              discountTiers.map((tier, index) => {
                                // For tiers with MaxMonths = int.MaxValue, show as "12+ months"
                                const isMaxTier = tier.maxMonths > 1000;
                                const label = isMaxTier
                                  ? `${tier.minMonths}+ tháng${tier.discountPercentage > 0 ? ` (Giảm ${tier.discountPercentage}%)` : ''}`
                                  : tier.minMonths === tier.maxMonths
                                    ? `${tier.minMonths} tháng${tier.discountPercentage > 0 ? ` (Giảm ${tier.discountPercentage}%)` : ''}`
                                    : `${tier.minMonths}-${tier.maxMonths} tháng${tier.discountPercentage > 0 ? ` (Giảm ${tier.discountPercentage}%)` : ''}`;

                                return (
                                  <option key={index} value={tier.minMonths}>
                                    {label}
                                  </option>
                                );
                              })
                            ) : (
                              // Fallback options if discount tiers are not loaded
                              <>
                                <option value="1">1 tháng</option>
                                <option value="2">2 tháng</option>
                                <option value="3">3 tháng (Giảm 10%)</option>
                                <option value="6">6 tháng (Giảm 20%)</option>
                                <option value="12">12 tháng (Giảm 40%)</option>
                              </>
                            )}
                          </Form.Select>
                        </Form.Group>
                      </Col>
                    </Row>

                    <div className="d-flex justify-content-end mt-4">
                      <Button
                        variant="primary"
                        type="submit"
                        className="px-4 py-2 fw-bold shadow-sm"
                        disabled={loading}
                      >
                        {loading ? (
                          <>
                            <Spinner
                              as="span"
                              animation="border"
                              size="sm"
                              role="status"
                              aria-hidden="true"
                            />
                            {' '}Đang xử lý...
                          </>
                        ) : <><FaPlus className="me-2" />Đăng ký</>}
                      </Button>
                    </div>
                  </Form>
                </Card.Body>
              </Card>
            </Col>

            <Col md={4}>
              <Card>
                <Card.Header>
                  <h5 className="mb-0">Thông tin gói đăng ký</h5>
                </Card.Header>
                <Card.Body>
                  <div className="mb-3">
                    <h6>Loại xe:</h6>
                    <p className="mb-0">{formData.vehicleType === 'CAR' ? 'Xe ô tô' : 'Xe máy'}</p>
                  </div>

                  <div className="mb-3">
                    <h6>Thời hạn:</h6>
                    <p className="mb-0">{formData.packageDuration} tháng</p>
                  </div>

                  <div className="mb-3">
                    <h6>Giá gói cơ bản:</h6>
                    <p className="mb-0">{packagePrice.basePrice?.toLocaleString()} VND / tháng</p>
                  </div>

                  <div className="mb-3">
                    <h6>Tổng tiền trước giảm giá:</h6>
                    <p className="mb-0">{packagePrice.totalBeforeDiscount?.toLocaleString()} VND</p>
                  </div>

                  {packagePrice.discountPercentage > 0 && (
                    <div className="mb-3">
                      <h6>Giảm giá:</h6>
                      <p className="mb-0 text-danger">-{packagePrice.discountPercentage}% ({(packagePrice.totalBeforeDiscount - packagePrice.price)?.toLocaleString()} VND)</p>
                    </div>
                  )}

                  <div className="mb-3">
                    <h6>Thành tiền:</h6>
                    <h4 className="text-primary">{packagePrice.price?.toLocaleString()} VND</h4>
                  </div>

                  <div className="alert alert-info">
                    <small>
                      <strong>Lưu ý:</strong> Giảm giá được áp dụng theo thời hạn đăng ký.
                      <ul className="mb-0 mt-1">
                        {discountTiers.length > 0 ? (
                          discountTiers.map((tier, index) => {
                            // Skip tiers with 0% discount
                            if (tier.discountPercentage === 0) return null;

                            // For tiers with MaxMonths = int.MaxValue, show as "12+ months"
                            const isMaxTier = tier.maxMonths > 1000;
                            const label = isMaxTier
                              ? `Gói ${tier.minMonths}+ tháng: Giảm ${tier.discountPercentage}%`
                              : tier.minMonths === tier.maxMonths
                                ? `Gói ${tier.minMonths} tháng: Giảm ${tier.discountPercentage}%`
                                : `Gói ${tier.minMonths}-${tier.maxMonths} tháng: Giảm ${tier.discountPercentage}%`;

                            return <li key={index}>{label}</li>;
                          })
                        ) : (
                          // Fallback if discount tiers are not loaded
                          <>
                            <li>Gói 3-5 tháng: Giảm 10%</li>
                            <li>Gói 6-11 tháng: Giảm 20%</li>
                            <li>Gói 12 tháng trở lên: Giảm 40%</li>
                          </>
                        )}
                      </ul>
                    </small>
                  </div>
                </Card.Body>
              </Card>
            </Col>
          </Row>
        </Tab>

        <Tab eventKey="vehicles" title="Danh sách xe đăng ký">
          <Card>
            <Card.Header>
              <div className="d-flex justify-content-between align-items-center">
                <h5 className="mb-0">Danh sách xe đăng ký</h5>
                <Button variant="primary" onClick={() => setActiveTab('registration')}>
                  <FaPlus className="me-1" /> Đăng ký mới
                </Button>
              </div>
            </Card.Header>
            <Card.Body>
              <Row className="mb-3">
                <Col md={6}>
                  <Form.Group>
                    <Form.Control
                      type="text"
                      placeholder="Tìm kiếm theo biển số, tên khách hàng..."
                      value={searchTerm}
                      onChange={(e) => setSearchTerm(e.target.value)}
                    />
                  </Form.Group>
                </Col>
                <Col md={3}>
                  <Form.Select
                    value={statusFilter}
                    onChange={(e) => setStatusFilter(e.target.value)}
                  >
                    <option value="ALL">Tất cả trạng thái</option>
                    <option value="VALID">Còn hạn</option>
                    <option value="EXPIRED">Hết hạn</option>
                    <option value="CANCELLED">Đã hủy</option>
                  </Form.Select>
                </Col>
                <Col md={3} className="text-end">
                  <Button variant="outline-secondary" onClick={fetchVehicles}>
                    <FaFilter className="me-1" /> Lọc
                  </Button>
                </Col>
              </Row>

              {vehiclesLoading ? (
                <div className="text-center my-5">
                  <Spinner animation="border" role="status">
                    <span className="visually-hidden">Đang tải...</span>
                  </Spinner>
                </div>
              ) : filteredVehicles.length === 0 ? (
                <Alert variant="info">Không tìm thấy xe đăng ký nào</Alert>
              ) : (
                <div className="table-responsive">
                  <Table striped bordered hover>
                    <thead>
                      <tr>
                        <th>Mã xe</th>
                        <th>Biển số</th>
                        <th>Loại xe</th>
                        <th>Khách hàng</th>
                        <th>Gói đăng ký</th>
                        <th>Ngày bắt đầu</th>
                        <th>Ngày hết hạn</th>
                        <th>Trạng thái</th>
                        <th>Vị trí cố định</th>
                        <th>Thao tác</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredVehicles.map((vehicle) => (
                        <tr key={vehicle.id}>
                          <td>{vehicle.vehicleId}</td>
                          <td>{vehicle.licensePlate}</td>
                          <td>{getVehicleTypeBadge(vehicle.vehicleType)}</td>
                          <td>
                            <div>{vehicle.customerName}</div>
                            <small className="text-muted">{vehicle.customerPhone}</small>
                          </td>
                          <td>
                            <div>{vehicle.packageDuration} tháng</div>
                            <small className="text-muted">{vehicle.packageAmount?.toLocaleString()} VND</small>
                          </td>
                          <td>{formatDate(vehicle.startDate)}</td>
                          <td>{formatDate(vehicle.endDate)}</td>
                          <td>
                            {getStatusBadge(vehicle.status)}
                            {vehicle.status === 'VALID' && (
                              <div className="mt-1">
                                <small className="text-muted">Còn {calculateDaysLeft(vehicle.endDate)} ngày</small>
                              </div>
                            )}
                          </td>
                          <td>
                            {vehicle.fixedSlotId ? (
                              <Badge bg="warning">{vehicle.fixedSlotId}</Badge>
                            ) : (
                              <span className="text-muted">Không có</span>
                            )}
                          </td>
                          <td>
                            {vehicle.status === 'VALID' && (
                              <>
                                <Button
                                  variant="outline-primary"
                                  size="sm"
                                  className="me-1 mb-1"
                                  onClick={() => openRenewalModal(vehicle)}
                                >
                                  <FaEdit className="me-1" /> Gia hạn
                                </Button>
                                <Button
                                  variant="outline-danger"
                                  size="sm"
                                  className="mb-1"
                                  onClick={() => handleCancel(vehicle.id)}
                                >
                                  <FaTrash className="me-1" /> Hủy
                                </Button>
                              </>
                            )}
                            {vehicle.status === 'EXPIRED' && (
                              <Button
                                variant="outline-success"
                                size="sm"
                                onClick={() => openRenewalModal(vehicle)}
                              >
                                <FaEdit className="me-1" /> Gia hạn
                              </Button>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </div>
              )}
            </Card.Body>
          </Card>
        </Tab>
      </Tabs>

      {/* Renewal Modal */}
      <Modal show={showRenewalModal} onHide={() => setShowRenewalModal(false)}>
        <Modal.Header closeButton>
          <Modal.Title>Gia hạn đăng ký xe tháng</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {error && <Alert variant="danger">{error}</Alert>}

          <Form>
            <Form.Group className="mb-3">
              <Form.Label>Thời hạn gia hạn</Form.Label>
              <Form.Select
                name="packageDuration"
                value={renewalData.packageDuration}
                onChange={handleRenewalInputChange}
              >
                {/* Generate options based on discount tiers */}
                {discountTiers.length > 0 ? (
                  discountTiers.map((tier, index) => {
                    // For tiers with MaxMonths = int.MaxValue, show as "12+ months"
                    const isMaxTier = tier.maxMonths > 1000;
                    const label = isMaxTier
                      ? `${tier.minMonths}+ tháng${tier.discountPercentage > 0 ? ` (Giảm ${tier.discountPercentage}%)` : ''}`
                      : tier.minMonths === tier.maxMonths
                        ? `${tier.minMonths} tháng${tier.discountPercentage > 0 ? ` (Giảm ${tier.discountPercentage}%)` : ''}`
                        : `${tier.minMonths}-${tier.maxMonths} tháng${tier.discountPercentage > 0 ? ` (Giảm ${tier.discountPercentage}%)` : ''}`;

                    return (
                      <option key={index} value={tier.minMonths}>
                        {label}
                      </option>
                    );
                  })
                ) : (
                  // Fallback options if discount tiers are not loaded
                  <>
                    <option value="1">1 tháng</option>
                    <option value="2">2 tháng</option>
                    <option value="3">3 tháng (Giảm 10%)</option>
                    <option value="6">6 tháng (Giảm 20%)</option>
                    <option value="12">12 tháng (Giảm 40%)</option>
                  </>
                )}
              </Form.Select>
            </Form.Group>

            <div className="mb-3">
              <h6>Thành tiền:</h6>
              <h4 className="text-primary">{renewalPrice.price?.toLocaleString()} VND</h4>

              {renewalPrice.discountPercentage > 0 && (
                <div className="text-success">
                  <small>Đã giảm {renewalPrice.discountPercentage}%</small>
                </div>
              )}
            </div>
          </Form>
        </Modal.Body>
        <Modal.Footer>
          <Button variant="secondary" onClick={() => setShowRenewalModal(false)}>
            Hủy
          </Button>
          <Button
            variant="primary"
            onClick={handleRenewal}
            disabled={loading}
          >
            {loading ? (
              <>
                <Spinner
                  as="span"
                  animation="border"
                  size="sm"
                  role="status"
                  aria-hidden="true"
                />
                {' '}Đang xử lý...
              </>
            ) : 'Xác nhận gia hạn'}
          </Button>
        </Modal.Footer>
      </Modal>

      {/* Payment Modal */}
      {showPaymentModal && selectedVehicle && (
        <PaymentModal
          show={showPaymentModal}
          onHide={() => setShowPaymentModal(false)}
          vehicleData={selectedVehicle}
          paymentType={paymentType}
          onPaymentComplete={() => {
            setShowPaymentModal(false);
            fetchVehicles();

            // Reset form if it was a registration
            if (paymentType === 'registration') {
              setFormData({
                licensePlate: '',
                vehicleType: 'MOTORBIKE',
                customerName: '',
                customerPhone: '',
                customerEmail: '',
                packageDuration: 1
              });
              setSuccess('Đăng ký xe tháng thành công!');
            } else {
              setSuccess('Gia hạn xe tháng thành công!');
            }
          }}
        />
      )}
    </Container>
  );
};

export default MonthlyRegistration;
