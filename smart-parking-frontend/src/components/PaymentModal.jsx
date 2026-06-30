import React, { useState } from 'react';
import { Modal, Button, Form, Alert, Spinner, Tabs, Tab, Row, Col } from 'react-bootstrap';
import { FaCreditCard, FaMoneyBill, FaMobileAlt } from 'react-icons/fa';
import axios from 'axios';
import { toast } from 'react-toastify';
import StripeCardForm from './StripeCardForm';

const PaymentModal = ({ show, onHide, vehicleData, onPaymentComplete, paymentType = 'registration' }) => {
  const [paymentMethod, setPaymentMethod] = useState('cash');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [stripeClientSecret, setStripeClientSecret] = useState(null);
  const [momoPaymentUrl, setMomoPaymentUrl] = useState(null);
  const [transactionId, setTransactionId] = useState(null);

  const handleCashPayment = async () => {
    try {
      setLoading(true);
      setError(null);

      // Use the new cash payment endpoint
      const response = await axios.post('/api/monthlyvehicle/payment/cash', {
        vehicleId: paymentType === 'registration' ? vehicleData.registrationId : vehicleData.renewalId,
        isRenewal: paymentType === 'renewal'
      });

      // Show success message
      toast.success(paymentType === 'registration'
        ? 'Đăng ký xe tháng thành công!'
        : 'Gia hạn xe tháng thành công!');

      // Close modal and notify parent
      onHide();
      onPaymentComplete();
    } catch (err) {
      console.error('Error processing cash payment:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi xử lý thanh toán');
    } finally {
      setLoading(false);
    }
  };

  const handleMomoPayment = async () => {
    try {
      setLoading(true);
      setError(null);

      // Create Momo payment
      const response = await axios.post('/api/monthlyvehicle/payment/momo', {
        vehicleId: paymentType === 'registration' ? vehicleData.registrationId : vehicleData.renewalId,
        isRenewal: paymentType === 'renewal'
      });

      // Store transaction ID and payment URL
      setTransactionId(response.data.transactionId);
      setMomoPaymentUrl(response.data.paymentUrl);

      // Open payment URL in new window
      window.open(response.data.paymentUrl, '_blank');
    } catch (err) {
      console.error('Error creating Momo payment:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi tạo thanh toán Momo');
    } finally {
      setLoading(false);
    }
  };

  const handleStripePayment = async () => {
    try {
      setLoading(true);
      setError(null);

      // Create Stripe payment
      const response = await axios.post('/api/monthlyvehicle/payment/stripe', {
        vehicleId: paymentType === 'registration' ? vehicleData.registrationId : vehicleData.renewalId,
        isRenewal: paymentType === 'renewal'
      });

      // Store client secret — the Stripe Elements card form renders once this is set.
      setStripeClientSecret(response.data.clientSecret);
      setTransactionId(response.data.transactionId);
    } catch (err) {
      console.error('Error creating Stripe payment:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi tạo thanh toán Stripe');
    } finally {
      setLoading(false);
    }
  };

  // Called by StripeCardForm once the card payment is confirmed by Stripe.
  // The webhook completes the transaction server-side; we poll its status here.
  const handleStripeSuccess = async () => {
    toast.success('Thanh toán thẻ thành công. Đang xác nhận...');
    await checkPaymentStatus();
  };

  const checkPaymentStatus = async () => {
    try {
      setLoading(true);
      setError(null);

      // Check payment status
      const response = await axios.get(`/api/transactions/${transactionId}/status`);

      if (response.data.status === 'COMPLETED') {
        // Show success message
        toast.success(paymentType === 'registration'
          ? 'Đăng ký xe tháng thành công!'
          : 'Gia hạn xe tháng thành công!');

        // Close modal and notify parent
        onHide();
        onPaymentComplete();
      } else if (response.data.status === 'PENDING') {
        toast.info('Thanh toán đang được xử lý. Vui lòng thử lại sau.');
      } else {
        setError('Thanh toán không thành công. Vui lòng thử lại.');
      }
    } catch (err) {
      console.error('Error checking payment status:', err);
      setError(err.response?.data?.error || 'Đã xảy ra lỗi khi kiểm tra trạng thái thanh toán');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal show={show} onHide={onHide} size="lg" centered>
      <Modal.Header closeButton>
        <Modal.Title>
          {paymentType === 'registration' ? 'Thanh toán đăng ký xe tháng' : 'Thanh toán gia hạn xe tháng'}
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <Alert variant="danger">{error}</Alert>}

        <div className="mb-4">
          <h5>Thông tin xe</h5>
          <Row>
            <Col md={6}>
              <p><strong>Biển số:</strong> {vehicleData.licensePlate}</p>
              <p><strong>Loại xe:</strong> {vehicleData.vehicleType === 'CAR' ? 'Xe ô tô' : 'Xe máy'}</p>
            </Col>
            <Col md={6}>
              <p><strong>Thời hạn:</strong> {vehicleData.packageDuration} tháng</p>
              <p><strong>Số tiền:</strong> {vehicleData.packageAmount?.toLocaleString()} VND</p>
            </Col>
          </Row>
        </div>

        <Tabs
          activeKey={paymentMethod}
          onSelect={(k) => setPaymentMethod(k)}
          className="mb-3"
        >
          <Tab eventKey="cash" title={<><FaMoneyBill className="me-2" />Tiền mặt</>}>
            <div className="p-3">
              <p>Thanh toán bằng tiền mặt tại quầy.</p>
              <Button
                variant="success"
                onClick={handleCashPayment}
                disabled={loading}
                className="mt-2"
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
                ) : 'Xác nhận thanh toán tiền mặt'}
              </Button>
            </div>
          </Tab>

          <Tab eventKey="momo" title={<><FaMobileAlt className="me-2" />Momo</>}>
            <div className="p-3">
              {momoPaymentUrl ? (
                <div>
                  <p>Vui lòng hoàn tất thanh toán Momo trong cửa sổ mới đã mở.</p>
                  <p>Sau khi thanh toán xong, nhấn nút kiểm tra bên dưới.</p>
                  <div className="d-flex gap-2 mt-3">
                    <Button
                      variant="primary"
                      onClick={() => window.open(momoPaymentUrl, '_blank')}
                    >
                      Mở lại trang thanh toán
                    </Button>
                    <Button
                      variant="success"
                      onClick={checkPaymentStatus}
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
                          {' '}Đang kiểm tra...
                        </>
                      ) : 'Kiểm tra trạng thái thanh toán'}
                    </Button>
                  </div>
                </div>
              ) : (
                <div>
                  <p>Thanh toán qua ví điện tử Momo.</p>
                  <Button
                    variant="primary"
                    onClick={handleMomoPayment}
                    disabled={loading}
                    className="mt-2"
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
                    ) : 'Tạo thanh toán Momo'}
                  </Button>
                </div>
              )}
            </div>
          </Tab>

          <Tab eventKey="stripe" title={<><FaCreditCard className="me-2" />Thẻ tín dụng</>}>
            <div className="p-3">
              {stripeClientSecret ? (
                <div>
                  <p>Vui lòng nhập thông tin thẻ để hoàn tất thanh toán.</p>
                  <StripeCardForm
                    clientSecret={stripeClientSecret}
                    onSuccess={handleStripeSuccess}
                    onError={(err) => setError(err.message)}
                  />
                  <Button
                    variant="link"
                    onClick={checkPaymentStatus}
                    disabled={loading}
                    className="mt-2 p-0"
                  >
                    {loading ? 'Đang kiểm tra...' : 'Kiểm tra lại trạng thái thanh toán'}
                  </Button>
                </div>
              ) : (
                <div>
                  <p>Thanh toán bằng thẻ tín dụng/ghi nợ quốc tế.</p>
                  <Button
                    variant="primary"
                    onClick={handleStripePayment}
                    disabled={loading}
                    className="mt-2"
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
                    ) : 'Tạo thanh toán thẻ'}
                  </Button>
                </div>
              )}
            </div>
          </Tab>
        </Tabs>
      </Modal.Body>
      <Modal.Footer>
        <Button variant="secondary" onClick={onHide} disabled={loading}>
          Hủy
        </Button>
      </Modal.Footer>
    </Modal>
  );
};

export default PaymentModal;
