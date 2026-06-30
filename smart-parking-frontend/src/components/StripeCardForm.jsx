import React, { useState } from 'react';
import { Elements, PaymentElement, useStripe, useElements } from '@stripe/react-stripe-js';
import { Button, Alert, Spinner } from 'react-bootstrap';
import { stripePromise, isStripeConfigured } from '../utils/stripeConfig';

// Inner form — must be rendered inside <Elements>. Collects the card via Stripe's
// PaymentElement and confirms the PaymentIntent identified by the parent's clientSecret.
const CardFormInner = ({ onSuccess, onError }) => {
  const stripe = useStripe();
  const elements = useElements();
  const [processing, setProcessing] = useState(false);
  const [message, setMessage] = useState(null);

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!stripe || !elements) return; // Stripe.js not loaded yet.

    setProcessing(true);
    setMessage(null);

    // Confirm without a full-page redirect; handle the result inline.
    const { error, paymentIntent } = await stripe.confirmPayment({
      elements,
      redirect: 'if_required',
    });

    if (error) {
      setMessage(error.message || 'Thanh toán thất bại. Vui lòng thử lại.');
      setProcessing(false);
      if (onError) onError(error);
      return;
    }

    if (paymentIntent && paymentIntent.status === 'succeeded') {
      setProcessing(false);
      if (onSuccess) onSuccess(paymentIntent);
    } else {
      // e.g. requires_action that resolved, or a pending state.
      setMessage(`Trạng thái thanh toán: ${paymentIntent?.status ?? 'không xác định'}`);
      setProcessing(false);
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <PaymentElement />
      {message && <Alert variant="danger" className="mt-3 mb-0">{message}</Alert>}
      <Button type="submit" variant="primary" className="w-100 mt-3" disabled={!stripe || processing}>
        {processing ? (
          <><Spinner as="span" animation="border" size="sm" className="me-2" />Đang xử lý...</>
        ) : (
          'Thanh toán bằng thẻ'
        )}
      </Button>
    </form>
  );
};

// Public wrapper — provides the Stripe <Elements> context bound to the clientSecret.
// Renders a configuration notice when no publishable key is set.
const StripeCardForm = ({ clientSecret, onSuccess, onError }) => {
  if (!isStripeConfigured || !stripePromise) {
    return (
      <Alert variant="warning" className="mb-0">
        Thanh toán thẻ (Stripe) chưa được cấu hình. Vui lòng đặt biến môi trường
        <code className="mx-1">VITE_STRIPE_PUBLISHABLE_KEY</code>
        hoặc dùng phương thức Tiền mặt / Momo.
      </Alert>
    );
  }

  if (!clientSecret) {
    return (
      <Alert variant="info" className="mb-0">
        Đang khởi tạo thanh toán thẻ...
      </Alert>
    );
  }

  return (
    <Elements stripe={stripePromise} options={{ clientSecret }}>
      <CardFormInner onSuccess={onSuccess} onError={onError} />
    </Elements>
  );
};

export default StripeCardForm;
