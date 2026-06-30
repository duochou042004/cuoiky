import React, { useState } from 'react';
import { Modal, Button, Form, Alert, Spinner, Card, Image } from 'react-bootstrap';
import axios from 'axios';
import StripeCardForm from '../components/StripeCardForm';

const PaymentModal = ({ show, onHide, vehicleData, onPaymentComplete }) => {
  const [paymentMethod, setPaymentMethod] = useState('CASH');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(false);
  const [momoPaymentData, setMomoPaymentData] = useState(null);
  const [momoPaymentStep, setMomoPaymentStep] = useState(0); // 0: initial, 1: QR displayed, 2: payment completed
  const [stripeClientSecret, setStripeClientSecret] = useState(null);
  const [stripeTransactionId, setStripeTransactionId] = useState(null);

  const handlePayment = async () => {
    setLoading(true);
    setError(null);

    try {
      // Process payment based on selected method
      if (paymentMethod === 'CASH') {
        const response = await axios.post('/api/payment/cash', {
          vehicleId: vehicleData.vehicle.vehicleId,
          amount: vehicleData.parkingFee,
          paymentMethod: 'CASH',
          description: `Parking fee for ${vehicleData.vehicle.vehicleType} with license plate ${vehicleData.vehicle.licensePlate}`
        });

        // Complete checkout with payment confirmation
        const checkoutResponse = await axios.post(`/api/vehicle/checkout/${vehicleData.vehicle.vehicleId}`, {
          paymentConfirmed: true,
          transactionId: response.data.transaction.transactionId
        });

        setSuccess(true);

        // Notify parent component that payment is complete
        if (onPaymentComplete) {
          onPaymentComplete(checkoutResponse.data);
        }
      } else if (paymentMethod === 'MOMO') {
        // Create Momo payment request
        const response = await axios.post('/api/payment/momo', {
          vehicleId: vehicleData.vehicle.vehicleId,
          amount: vehicleData.parkingFee,
          paymentMethod: 'MOMO'
        });

        // Store payment data and update step
        setMomoPaymentData(response.data);
        setMomoPaymentStep(1);
      } else if (paymentMethod === 'STRIPE') {
        // Create the Stripe PaymentIntent (server creates a pending transaction).
        const response = await axios.post('/api/payment/stripe', {
          vehicleId: vehicleData.vehicle.vehicleId,
          amount: vehicleData.parkingFee,
          paymentMethod: 'STRIPE'
        });

        const txId = response.data.transaction?.transactionId;
        setStripeTransactionId(txId);

        if (response.data.mockMode) {
          // Mock mode: the server already marked the transaction completed.
          await completeCheckout(txId);
        } else {
          // Live mode: render the card form; checkout runs after card confirmation.
          setStripeClientSecret(response.data.clientSecret);
        }
      } else {
        setError('Selected payment method is not yet supported.');
      }
    } catch (error) {
      console.error('Payment error:', error);
      setError(error.response?.data?.error || 'An error occurred during payment processing');
    } finally {
      setLoading(false);
    }
  };

  const handleMomoPaymentComplete = async () => {
    setLoading(true);
    setError(null);

    try {
      // Check transaction status
      const transactionResponse = await axios.get(`/api/payment/transactions/${momoPaymentData.transaction.transactionId}`);

      if (transactionResponse.data.status === 'COMPLETED') {
        // Complete checkout with payment confirmation
        const checkoutResponse = await axios.post(`/api/vehicle/checkout/${vehicleData.vehicle.vehicleId}`, {
          paymentConfirmed: true,
          transactionId: momoPaymentData.transaction.transactionId
        });

        setSuccess(true);
        setMomoPaymentStep(2);

        // Notify parent component that payment is complete
        if (onPaymentComplete) {
          onPaymentComplete(checkoutResponse.data);
        }
      } else {
        setError('Payment has not been completed yet. Please complete the payment using the Momo app.');
      }
    } catch (error) {
      console.error('Payment verification error:', error);
      setError(error.response?.data?.error || 'An error occurred during payment verification');
    } finally {
      setLoading(false);
    }
  };

  // Finalize the casual-vehicle checkout once a payment transaction is completed.
  const completeCheckout = async (transactionId) => {
    const checkoutResponse = await axios.post(`/api/vehicle/checkout/${vehicleData.vehicle.vehicleId}`, {
      paymentConfirmed: true,
      transactionId
    });
    setSuccess(true);
    if (onPaymentComplete) {
      onPaymentComplete(checkoutResponse.data);
    }
  };

  // Called by StripeCardForm after the card payment is confirmed (live mode).
  const handleStripeCardSuccess = async () => {
    setLoading(true);
    setError(null);
    try {
      await completeCheckout(stripeTransactionId);
    } catch (err) {
      console.error('Checkout after Stripe payment failed:', err);
      setError(err.response?.data?.error || 'Đã thanh toán nhưng không thể hoàn tất việc xuất xe.');
    } finally {
      setLoading(false);
    }
  };

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
  };

  return (
    <Modal show={show} onHide={onHide} backdrop="static" keyboard={false} centered>
      <Modal.Header closeButton={!loading}>
        <Modal.Title>Payment Required</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {success ? (
          <Alert variant="success">
            <Alert.Heading>Payment Successful!</Alert.Heading>
            <p>
              The payment for vehicle {vehicleData?.vehicle?.licensePlate} has been processed successfully.
              The vehicle has been checked out.
            </p>
          </Alert>
        ) : momoPaymentStep === 1 ? (
          <>
            <Card className="mb-3">
              <Card.Body className="text-center">
                <h5>Momo Payment</h5>
                <p>Scan the QR code below with your Momo app to complete the payment.</p>
                <div className="d-flex justify-content-center my-3">
                  <Image src={momoPaymentData?.qrCodeUrl} alt="Momo QR Code" fluid style={{ maxWidth: '250px' }} />
                </div>
                <p className="mb-1"><strong>Amount:</strong> {formatCurrency(vehicleData?.parkingFee)}</p>
                <p className="mb-1"><strong>Transaction ID:</strong> {momoPaymentData?.transaction?.transactionId}</p>
                <div className="mt-3">
                  <Button variant="primary" href={momoPaymentData?.paymentUrl} target="_blank" className="me-2">
                    Open Momo App
                  </Button>
                </div>
              </Card.Body>
            </Card>
            {error && (
              <Alert variant="danger" className="mt-3">
                {error}
              </Alert>
            )}
          </>
        ) : stripeClientSecret ? (
          <>
            <Card className="mb-3">
              <Card.Body>
                <h5>Thanh toán bằng thẻ</h5>
                <p className="mb-1"><strong>Amount:</strong> {formatCurrency(vehicleData?.parkingFee)}</p>
                <StripeCardForm
                  clientSecret={stripeClientSecret}
                  onSuccess={handleStripeCardSuccess}
                  onError={(err) => setError(err.message)}
                />
              </Card.Body>
            </Card>
            {error && (
              <Alert variant="danger" className="mt-3">
                {error}
              </Alert>
            )}
          </>
        ) : (
          <>
            <Card className="mb-3">
              <Card.Body>
                <h5>Vehicle Information</h5>
                <p className="mb-1"><strong>License Plate:</strong> {vehicleData?.vehicle?.licensePlate}</p>
                <p className="mb-1"><strong>Vehicle Type:</strong> {vehicleData?.vehicle?.vehicleType}</p>
                <p className="mb-1"><strong>Entry Time:</strong> {new Date(vehicleData?.vehicle?.entryTime).toLocaleString()}</p>
                <p className="mb-1"><strong>Parking Duration:</strong> {vehicleData?.parkingDuration}</p>
                <h5 className="mt-3">Amount Due: {formatCurrency(vehicleData?.parkingFee)}</h5>
              </Card.Body>
            </Card>

            <Form>
              <Form.Group className="mb-3">
                <Form.Label>Select Payment Method</Form.Label>
                <div>
                  <Form.Check
                    type="radio"
                    label="Cash"
                    name="paymentMethod"
                    id="cash"
                    value="CASH"
                    checked={paymentMethod === 'CASH'}
                    onChange={() => setPaymentMethod('CASH')}
                    className="mb-2"
                  />
                  <Form.Check
                    type="radio"
                    label="Momo E-Wallet"
                    name="paymentMethod"
                    id="momo"
                    value="MOMO"
                    checked={paymentMethod === 'MOMO'}
                    onChange={() => setPaymentMethod('MOMO')}
                    className="mb-2"
                  />
                  <Form.Check
                    type="radio"
                    label="Thẻ tín dụng/ghi nợ (Stripe)"
                    name="paymentMethod"
                    id="stripe"
                    value="STRIPE"
                    checked={paymentMethod === 'STRIPE'}
                    onChange={() => setPaymentMethod('STRIPE')}
                    className="mb-2"
                  />
                </div>
              </Form.Group>
            </Form>

            {error && (
              <Alert variant="danger" className="mt-3">
                {error}
              </Alert>
            )}
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        {success ? (
          <Button variant="primary" onClick={onHide}>
            Close
          </Button>
        ) : momoPaymentStep === 1 ? (
          <>
            <Button variant="secondary" onClick={() => {
              setMomoPaymentStep(0);
              setMomoPaymentData(null);
            }} disabled={loading}>
              Back
            </Button>
            <Button variant="primary" onClick={handleMomoPaymentComplete} disabled={loading}>
              {loading ? (
                <>
                  <Spinner
                    as="span"
                    animation="border"
                    size="sm"
                    role="status"
                    aria-hidden="true"
                  />
                  {' '}Verifying...
                </>
              ) : 'I\'ve Paid'}
            </Button>
          </>
        ) : stripeClientSecret ? (
          <Button variant="secondary" onClick={() => { setStripeClientSecret(null); setStripeTransactionId(null); }} disabled={loading}>
            Back
          </Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onHide} disabled={loading}>
              Cancel
            </Button>
            <Button variant="primary" onClick={handlePayment} disabled={loading}>
              {loading ? (
                <>
                  <Spinner
                    as="span"
                    animation="border"
                    size="sm"
                    role="status"
                    aria-hidden="true"
                  />
                  {' '}Processing...
                </>
              ) : paymentMethod === 'MOMO' ? 'Continue to Momo Payment' : paymentMethod === 'STRIPE' ? 'Continue to Card Payment' : 'Process Payment'}
            </Button>
          </>
        )}
      </Modal.Footer>
    </Modal>
  );
};

export default PaymentModal;
