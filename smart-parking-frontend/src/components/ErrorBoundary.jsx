import React from 'react';

// Class-based error boundary — catches render/lifecycle errors in the subtree
// so one broken page does not blank out the whole app. There is no hooks-based
// equivalent for componentDidCatch, so this stays a class component.
class ErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error };
  }

  componentDidCatch(error, errorInfo) {
    // Log for diagnostics; in production this could go to a monitoring service.
    console.error('Uncaught error in component tree:', error, errorInfo);
  }

  handleReload = () => {
    this.setState({ hasError: false, error: null });
    window.location.reload();
  };

  render() {
    if (this.state.hasError) {
      return (
        <div
          style={{
            padding: '2rem',
            margin: '2rem auto',
            maxWidth: '640px',
            textAlign: 'center',
            border: '1px solid #f5c2c7',
            borderRadius: '0.5rem',
            background: '#f8d7da',
            color: '#842029',
          }}
        >
          <h2>Đã xảy ra lỗi</h2>
          <p>Trang gặp sự cố không mong muốn. Vui lòng tải lại trang hoặc thử lại sau.</p>
          {this.state.error?.message && (
            <pre
              style={{
                whiteSpace: 'pre-wrap',
                textAlign: 'left',
                background: '#fff',
                padding: '0.75rem',
                borderRadius: '0.25rem',
                fontSize: '0.85rem',
              }}
            >
              {this.state.error.message}
            </pre>
          )}
          <button
            type="button"
            onClick={this.handleReload}
            style={{
              marginTop: '1rem',
              padding: '0.5rem 1.25rem',
              border: 'none',
              borderRadius: '0.25rem',
              background: '#dc3545',
              color: '#fff',
              cursor: 'pointer',
            }}
          >
            Tải lại trang
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}

export default ErrorBoundary;
