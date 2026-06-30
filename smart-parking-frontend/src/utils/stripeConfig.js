import { loadStripe } from '@stripe/stripe-js';

// Publishable key comes from the Vite env (VITE_STRIPE_PUBLISHABLE_KEY).
// When it is absent the app runs without live card collection — the backend
// falls back to its Stripe mock mode and the UI shows a configuration notice.
const publishableKey = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY || '';

export const isStripeConfigured = Boolean(publishableKey);

// Load Stripe once and reuse the promise. Null when no key is configured.
export const stripePromise = isStripeConfigured ? loadStripe(publishableKey) : null;
