import React, { useState, useEffect } from 'react';
import { Row, Col, Card, Badge, Button } from 'react-bootstrap';
import axios from 'axios';
import * as signalR from '@microsoft/signalr';
import { Chart, registerables } from 'chart.js';
import { FaSync, FaExclamationTriangle, FaChartBar, FaCarAlt, FaMotorcycle, FaMoneyBillWave, FaUserCheck } from 'react-icons/fa';

// Import components
import StatCard from '../components/StatCard';
import ActivityChart from '../components/ActivityChart';
import ParkingSpaceStatus from '../components/ParkingSpaceStatus';
import ParkingLotVisualizer from '../components/ParkingLotVisualizer';
import RecentVehicles from '../components/RecentVehicles';

// Import styles
import '../styles/dashboard.css';
import '../styles/dashboard-modern.css';

// Register Chart.js components
Chart.register(...registerables);

const Dashboard = () => {
  // Mock data for when API calls fail
  const mockParkingSlots = [
    { slotId: 'M-01', status: 'AVAILABLE', type: 'MOTORBIKE', currentVehicleId: null },
    { slotId: 'M-02', status: 'OCCUPIED', type: 'MOTORBIKE', currentVehicleId: 'V001' },
    { slotId: 'M-03', status: 'AVAILABLE', type: 'MOTORBIKE', currentVehicleId: null },
    { slotId: 'M-04', status: 'RESERVED', type: 'MOTORBIKE', currentVehicleId: null },
    { slotId: 'M-05', status: 'AVAILABLE', type: 'MOTORBIKE', currentVehicleId: null },
    { slotId: 'C-01', status: 'AVAILABLE', type: 'CAR', currentVehicleId: null },
    { slotId: 'C-02', status: 'OCCUPIED', type: 'CAR', currentVehicleId: 'V002' },
    { slotId: 'C-03', status: 'AVAILABLE', type: 'CAR', currentVehicleId: null }
  ];

  const mockVehicles = [
    { vehicleId: 'V001', licensePlate: '61A-814.66', vehicleType: 'MOTORBIKE', entryTime: new Date().toISOString(), slotId: 'M-02', status: 'PARKING' },
    { vehicleId: 'V002', licensePlate: '32A-257.10', vehicleType: 'CAR', entryTime: new Date().toISOString(), slotId: 'C-02', status: 'PARKING' }
  ];

  const [parkingSlots, setParkingSlots] = useState([]);
  const [parkedVehicles, setParkedVehicles] = useState([]);
  const [recentVehicles, setRecentVehicles] = useState([]);
  const [vehiclesMap, setVehiclesMap] = useState({});
  const [connection, setConnection] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [expiringVehicles, setExpiringVehicles] = useState([]);
  const [stats, setStats] = useState({
    totalSlots: 0,
    availableSlots: 0,
    occupiedSlots: 0,
    motorcycleSlots: { total: 0, available: 0 },
    carSlots: { total: 0, available: 0 },
    monthlyVehicles: 0,
    reservedSlots: 0,
    revenue: 0
  });

  const [dateRange, setDateRange] = useState(7); // Default to 7 days
  const [activityData, setActivityData] = useState(generateMockActivityData());
  const [exitedVehiclesCount, setExitedVehiclesCount] = useState(0);
  const [dailyTrends, setDailyTrends] = useState({
    vehicles: { value: 5, isPositive: true },
    exits: { value: 8, isPositive: true },
    revenue: { value: 12, isPositive: true },
    monthly: { value: 0, isPositive: false }
  });

  // Generate mock activity data for the last 7 days
  function generateMockActivityData(days = 7) {
    const labels = [];
    const entryData = [];
    const exitData = [];

    const today = new Date();
    for (let i = 0; i < days; i++) {
      const date = new Date();
      date.setDate(today.getDate() - (days - 1) + i);
      labels.push(date.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' }));

      // Generate random data
      entryData.push(Math.floor(Math.random() * 20) + 5);
      exitData.push(Math.floor(Math.random() * 15) + 3);
    }

    return {
      labels,
      datasets: [
        {
          label: 'Xe vào',
          data: entryData,
          borderColor: '#0d6efd',
          backgroundColor: 'rgba(13, 110, 253, 0.1)',
          tension: 0.4
        },
        {
          label: 'Xe ra',
          data: exitData,
          borderColor: '#dc3545',
          backgroundColor: 'rgba(220, 53, 69, 0.1)',
          tension: 0.4
        }
      ]
    };
  };

  // Fetch parking slots
  const fetchParkingSlots = async () => {
    try {
      console.log('Fetching parking slots...');
      setLoading(true);
      setError(null);

      try {
        const response = await axios.get('/api/parking/slots');
        console.log('Parking slots response:', response.data);
        setParkingSlots(response.data);

        // Calculate stats
        const total = response.data.length;
        const available = response.data.filter(slot => slot.status === 'AVAILABLE').length;
        const reserved = response.data.filter(slot => slot.status === 'RESERVED').length;
        const occupied = total - available - reserved;

        const motorcycleTotal = response.data.filter(slot => slot.type === 'MOTORBIKE').length;
        const motorcycleAvailable = response.data.filter(slot => slot.type === 'MOTORBIKE' && slot.status === 'AVAILABLE').length;

        const carTotal = response.data.filter(slot => slot.type === 'CAR').length;
        const carAvailable = response.data.filter(slot => slot.type === 'CAR' && slot.status === 'AVAILABLE').length;

        // Fetch monthly vehicles count
        let monthlyVehicles = 0;
        try {
          const monthlyResponse = await axios.get('/api/monthlyvehicle/count');
          monthlyVehicles = monthlyResponse.data || 0;
          console.log('Monthly vehicles count:', monthlyVehicles);
        } catch (monthlyError) {
          console.error('Error fetching monthly vehicles count:', monthlyError);
          // Fallback to counting from parked vehicles
          monthlyVehicles = parkedVehicles.filter(v => v.isMonthlyRegistered).length;
        }

        // Generate mock trends (this will be replaced with real data in production)
        const mockTrends = {
          vehicles: { value: Math.floor(Math.random() * 15), isPositive: Math.random() > 0.3 },
          exits: { value: Math.floor(Math.random() * 15), isPositive: Math.random() > 0.3 },
          revenue: { value: Math.floor(Math.random() * 20), isPositive: Math.random() > 0.3 },
          monthly: { value: Math.floor(Math.random() * 5), isPositive: Math.random() > 0.5 }
        };

        setDailyTrends(mockTrends);

        // Fetch today's revenue
        try {
          const today = new Date();
          today.setHours(0, 0, 0, 0);

          const revenueResponse = await axios.get(`/api/transactions/summary?startDate=${today.toISOString()}`);
          const todayRevenue = revenueResponse.data.totalAmount || 0;

          setStats({
            totalSlots: total,
            availableSlots: available,
            occupiedSlots: occupied,
            motorcycleSlots: { total: motorcycleTotal, available: motorcycleAvailable },
            carSlots: { total: carTotal, available: carAvailable },
            monthlyVehicles: monthlyVehicles,
            reservedSlots: reserved,
            revenue: todayRevenue
          });
        } catch (revenueError) {
          console.error('Error fetching revenue:', revenueError);

          setStats({
            totalSlots: total,
            availableSlots: available,
            occupiedSlots: occupied,
            motorcycleSlots: { total: motorcycleTotal, available: motorcycleAvailable },
            carSlots: { total: carTotal, available: carAvailable },
            monthlyVehicles: monthlyVehicles,
            reservedSlots: reserved,
            revenue: 0
          });
        }

        // Fetch expiring monthly vehicles
        try {
          const expiringResponse = await axios.get('/api/monthlyvehicle/check-expiring?days=3');
          setExpiringVehicles(expiringResponse.data);
        } catch (expiringError) {
          console.error('Error fetching expiring vehicles:', expiringError);
          setExpiringVehicles([]);
        }
      } catch (apiError) {
        console.error('API error fetching parking slots:', apiError);
        setError('Không thể tải dữ liệu bãi đỗ xe. Vui lòng kiểm tra kết nối API.');
        setParkingSlots([]);
      }
    } catch (error) {
      console.error('Error in fetchParkingSlots:', error);
      setError('Không thể tải dữ liệu bãi đỗ xe. Vui lòng kiểm tra kết nối API.');
    } finally {
      setLoading(false);
    }
  };

  // Fetch exited vehicles count
  const fetchExitedVehiclesCount = async () => {
    try {
      console.log('Fetching exited vehicles count...');

      // Get today's date at midnight
      const today = new Date();
      today.setHours(0, 0, 0, 0);

      const response = await axios.get(`/api/vehicle/exited-count?startDate=${today.toISOString()}`);
      console.log('Exited vehicles count:', response.data);

      // Update the exited vehicles count
      setExitedVehiclesCount(response.data);
    } catch (error) {
      console.error('Error fetching exited vehicles count:', error);
      // Don't update the count if there's an error
    }
  };

  // Fetch parked vehicles
  const fetchParkedVehicles = async () => {
    try {
      console.log('Fetching parked vehicles...');
      setLoading(true);
      setError(null);

      try {
        const response = await axios.get('/api/parking/vehicles/parked');
        console.log('Parked vehicles response:', response.data);
        setParkedVehicles(response.data);

        // Add status field to each vehicle
        const vehiclesWithStatus = response.data.map(vehicle => ({
          ...vehicle,
          status: 'PARKING'
        }));

        setRecentVehicles(vehiclesWithStatus);

        // Create a map of vehicles indexed by vehicle ID
        const vehiclesMapData = {};
        response.data.forEach(vehicle => {
          vehiclesMapData[vehicle.vehicleId] = {
            licensePlate: vehicle.licensePlate,
            entryTime: vehicle.entryTime,
            vehicleType: vehicle.vehicleType,
            isMonthlyRegistered: vehicle.isMonthlyRegistered || false
          };
        });
        setVehiclesMap(vehiclesMapData);

        // Fetch recent vehicle history (last 24 hours)
        try {
          const startDate = new Date();
          startDate.setHours(startDate.getHours() - 24);

          const historyResponse = await axios.get(`/api/vehicle/history?startDate=${startDate.toISOString()}&status=EXITED`);

          // Add status field to each exited vehicle
          const exitedVehicles = historyResponse.data.map(vehicle => ({
            ...vehicle,
            status: 'EXITED'
          }));

          // Fetch the exited vehicles count separately
          await fetchExitedVehiclesCount();

          // Combine parked and recently exited vehicles, sort by entry time (most recent first)
          const allRecentVehicles = [...vehiclesWithStatus, ...exitedVehicles]
            .sort((a, b) => new Date(b.entryTime) - new Date(a.entryTime))
            .slice(0, 10); // Show only the 10 most recent vehicles

          setRecentVehicles(allRecentVehicles);
        } catch (historyError) {
          console.error('Error fetching vehicle history:', historyError);
          console.log('Using mock vehicle history data');
          setExitedVehiclesCount(3);
          setRecentVehicles(mockVehicles);
        }
      } catch (apiError) {
        console.error('API error fetching parked vehicles:', apiError);
        console.log('Using mock vehicle data instead');
        // Use mock data if API fails
        setParkedVehicles(mockVehicles);
        setRecentVehicles(mockVehicles);

        // Create a map of mock vehicles indexed by vehicle ID
        const mockVehiclesMap = {};
        mockVehicles.forEach(vehicle => {
          mockVehiclesMap[vehicle.vehicleId] = {
            licensePlate: vehicle.licensePlate,
            entryTime: vehicle.entryTime,
            vehicleType: vehicle.vehicleType,
            isMonthlyRegistered: false
          };
        });
        setVehiclesMap(mockVehiclesMap);

        setExitedVehiclesCount(3);
      }

      // Fetch activity data for the selected date range
      fetchActivityData();
    } catch (error) {
      console.error('Error in fetchParkedVehicles:', error);
      // Use mock data as fallback
      setParkedVehicles(mockVehicles);
      setRecentVehicles(mockVehicles);
      setExitedVehiclesCount(3);
      setError('Không thể tải dữ liệu xe. Đang hiển thị dữ liệu mẫu.');
    } finally {
      setLoading(false);
    }
  };

  // Fetch activity data for the selected date range
  const fetchActivityData = async () => {
    try {
      console.log('Fetching activity data for', dateRange, 'days');
      setLoading(true);

      const endDate = new Date();
      const startDate = new Date();
      startDate.setDate(startDate.getDate() - (dateRange - 1)); // Last N days including today

      // Format dates for labels
      const dateLabels = [];
      const entryData = [];
      const exitData = [];

      // Create array of dates for the selected date range
      for (let i = 0; i < dateRange; i++) {
        const date = new Date(startDate);
        date.setDate(date.getDate() + i);
        dateLabels.push(date.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' }));

        // Initialize with zeros, will be updated with actual data
        entryData.push(0);
        exitData.push(0);
      }

      try {
        // Fetch entry data
        const entryResponse = await axios.get(`/api/vehicle/history?startDate=${startDate.toISOString()}&endDate=${endDate.toISOString()}`);

        // Fetch exit data
        const exitResponse = await axios.get(`/api/vehicle/history?startDate=${startDate.toISOString()}&endDate=${endDate.toISOString()}&status=EXITED`);

        // Process entry data
        entryResponse.data.forEach(vehicle => {
          const entryDate = new Date(vehicle.entryTime);
          const dayIndex = Math.floor((entryDate - startDate) / (24 * 60 * 60 * 1000));
          if (dayIndex >= 0 && dayIndex < dateRange) {
            entryData[dayIndex]++;
          }
        });

        // Process exit data
        exitResponse.data.forEach(vehicle => {
          const exitDate = new Date(vehicle.exitTime);
          const dayIndex = Math.floor((exitDate - startDate) / (24 * 60 * 60 * 1000));
          if (dayIndex >= 0 && dayIndex < dateRange) {
            exitData[dayIndex]++;
          }
        });

        // Update activity data
        setActivityData({
          labels: dateLabels,
          datasets: [
            {
              label: 'Xe vào',
              data: entryData,
              borderColor: '#0d6efd',
              backgroundColor: 'rgba(13, 110, 253, 0.1)',
              tension: 0.4
            },
            {
              label: 'Xe ra',
              data: exitData,
              borderColor: '#dc3545',
              backgroundColor: 'rgba(220, 53, 69, 0.1)',
              tension: 0.4
            }
          ]
        });
      } catch (apiError) {
        console.error('API error fetching activity data:', apiError);
        console.log('Using mock activity data instead');
        // Use mock data if API fails
        setActivityData(generateMockActivityData(dateRange));
      }
    } catch (error) {
      console.error('Error in fetchActivityData:', error);
      // Use mock data as fallback
      setActivityData(generateMockActivityData(dateRange));
    } finally {
      setLoading(false);
    }
  };

  // Function to fetch revenue data
  const fetchRevenueData = async () => {
    try {
      console.log('Fetching revenue data...');
      const today = new Date();
      today.setHours(0, 0, 0, 0);

      const revenueResponse = await axios.get(`/api/transactions/summary?startDate=${today.toISOString()}`);
      const todayRevenue = revenueResponse.data.totalAmount || 0;

      // Update only the revenue in stats
      setStats(prevStats => ({
        ...prevStats,
        revenue: todayRevenue
      }));

      console.log('Updated revenue:', todayRevenue);
    } catch (error) {
      console.error('Error fetching revenue data:', error);
    }
  };

  // Function to refresh all dashboard data
  const refreshDashboard = () => {
    fetchParkingSlots();
    fetchParkedVehicles();
    fetchRevenueData();
    fetchExitedVehiclesCount();
  };

  // Set up periodic refresh
  useEffect(() => {
    // Initial data fetch
    fetchParkingSlots();
    fetchParkedVehicles();
    fetchRevenueData();
    fetchExitedVehiclesCount();

    // Set up interval to refresh data every 30 seconds
    const refreshInterval = setInterval(() => {
      console.log('Auto-refreshing dashboard data...');
      fetchParkingSlots();
      fetchParkedVehicles();
      fetchRevenueData();
      fetchExitedVehiclesCount();
    }, 30000);

    // Clean up interval on component unmount
    return () => clearInterval(refreshInterval);
  }, []);

  // SignalR connection setup
  useEffect(() => {
    // Set up SignalR connection
    try {
      console.log('Setting up SignalR connection...');
      const newConnection = new signalR.HubConnectionBuilder()
        .withUrl('/parkingHub')
        .withAutomaticReconnect()
        .build();

      newConnection.on('ReceiveParkingUpdate', (updatedSlot) => {
        console.log('Received parking update:', updatedSlot);
        setParkingSlots(prevSlots => {
          const newSlots = [...prevSlots];
          const index = newSlots.findIndex(slot => slot.slotId === updatedSlot.slotId);
          if (index !== -1) {
            newSlots[index] = updatedSlot;
          }
          return newSlots;
        });

        // Update stats when parking slot status changes
        setStats(prevStats => {
          // Calculate new available and occupied counts
          const available = prevStats.availableSlots +
            (updatedSlot.status === 'AVAILABLE' ? 1 : 0) -
            (updatedSlot.status !== 'AVAILABLE' ? 1 : 0);

          const occupied = prevStats.occupiedSlots +
            (updatedSlot.status === 'OCCUPIED' ? 1 : 0) -
            (updatedSlot.status !== 'OCCUPIED' ? 1 : 0);

          const reserved = prevStats.reservedSlots +
            (updatedSlot.status === 'RESERVED' ? 1 : 0) -
            (updatedSlot.status !== 'RESERVED' ? 1 : 0);

          return {
            ...prevStats,
            availableSlots: available,
            occupiedSlots: occupied,
            reservedSlots: reserved
          };
        });
      });

      newConnection.on('ReceiveVehicleUpdate', (updatedVehicle) => {
        console.log('Received vehicle update:', updatedVehicle);
        if (updatedVehicle.status === 'PARKED') {
          // Add to parked vehicles
          setParkedVehicles(prevVehicles => {
            const exists = prevVehicles.some(v => v.vehicleId === updatedVehicle.vehicleId);
            if (!exists) {
              return [...prevVehicles, updatedVehicle];
            }
            return prevVehicles;
          });

          // Add to recent vehicles with PARKING status
          const vehicleWithStatus = { ...updatedVehicle, status: 'PARKING' };
          setRecentVehicles(prevVehicles => {
            // Remove if already exists
            const filtered = prevVehicles.filter(v => v.vehicleId !== updatedVehicle.vehicleId);
            // Add to beginning of array and limit to 10
            return [vehicleWithStatus, ...filtered].slice(0, 10);
          });

          // Update vehicles map
          setVehiclesMap(prevMap => ({
            ...prevMap,
            [updatedVehicle.vehicleId]: {
              licensePlate: updatedVehicle.licensePlate,
              entryTime: updatedVehicle.entryTime,
              vehicleType: updatedVehicle.vehicleType,
              isMonthlyRegistered: updatedVehicle.isMonthlyRegistered || false
            }
          }));

          // Update stats when vehicle enters
          setStats(prevStats => ({
            ...prevStats,
            occupiedSlots: prevStats.occupiedSlots + 1,
            availableSlots: prevStats.availableSlots - 1
          }));

        } else if (updatedVehicle.status === 'LEFT') {
          // Remove from parked vehicles
          setParkedVehicles(prevVehicles =>
            prevVehicles.filter(v => v.vehicleId !== updatedVehicle.vehicleId)
          );

          // Update status in recent vehicles to EXITED
          setRecentVehicles(prevVehicles => {
            return prevVehicles.map(vehicle => {
              if (vehicle.vehicleId === updatedVehicle.vehicleId) {
                return { ...vehicle, status: 'EXITED', exitTime: updatedVehicle.exitTime };
              }
              return vehicle;
            });
          });

          // Remove from vehicles map
          setVehiclesMap(prevMap => {
            const newMap = { ...prevMap };
            delete newMap[updatedVehicle.vehicleId];
            return newMap;
          });

          // Update stats when vehicle exits
          setStats(prevStats => ({
            ...prevStats,
            occupiedSlots: prevStats.occupiedSlots - 1,
            availableSlots: prevStats.availableSlots + 1
          }));

          // Increment exited vehicles count
          setExitedVehiclesCount(prevCount => prevCount + 1);

          // Fetch updated revenue data when a vehicle exits
          fetchRevenueData();
        }
      });

      // Add a new event for transaction updates
      newConnection.on('ReceiveTransactionUpdate', (transaction) => {
        console.log('Received transaction update:', transaction);
        // Update revenue when a new transaction is created
        setStats(prevStats => ({
          ...prevStats,
          revenue: prevStats.revenue + (transaction.amount || 0)
        }));
      });

      newConnection.start()
        .then(() => {
          console.log('SignalR Connected');
          setConnection(newConnection);
        })
        .catch(err => {
          console.error('SignalR Connection Error:', err);
          // Continue without real-time updates
        });
    } catch (error) {
      console.error('Error setting up SignalR:', error);
      // Continue without real-time updates
    }

    return () => {
      try {
        if (connection) {
          console.log('Stopping SignalR connection...');
          connection.stop();
        }
      } catch (error) {
        console.error('Error stopping SignalR connection:', error);
      }
    };
  }, []);

  // Handle date range change
  const handleDateRangeChange = (days) => {
    console.log('Changing date range to', days, 'days');
    setDateRange(days);
    // Use setTimeout to ensure state is updated before calling fetchActivityData
    setTimeout(() => {
      fetchActivityData();
    }, 0);
  };

  // Calculate trend data for each statistic
  const getVehicleTrendData = () => [3, 5, 4, 7, 5, 8, stats.occupiedSlots];
  const getExitsTrendData = () => [5, 7, 6, 4, 7, 5, exitedVehiclesCount];
  const getRevenueTrendData = () => [300000, 450000, 500000, 350000, 600000, 400000, stats.revenue/1000];
  const getMonthlyTrendData = () => [7, 8, 9, 8, 7, 6, stats.monthlyVehicles];

  return (
    <div className="dashboard">
      <div className="d-flex justify-content-between align-items-center mb-4">
        <h1 className="mb-0">Trang chủ</h1>
        <div className="d-flex align-items-center">
          {expiringVehicles.length > 0 && (
            <div className="alert alert-warning py-1 px-3 me-3 mb-0 d-flex align-items-center">
              <FaExclamationTriangle className="me-2" />
              <small>
                <strong>{expiringVehicles.length} xe đăng ký tháng</strong> sắp hết hạn
              </small>
            </div>
          )}
          <Button
            variant="outline-primary"
            onClick={refreshDashboard}
            disabled={loading}
            className="d-flex align-items-center"
          >
            {loading ? (
              <>
                <span className="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
                <span>Đang tải...</span>
              </>
            ) : (
              <>
                <FaSync className="me-2" />
                <span>Làm mới</span>
              </>
            )}
          </Button>
        </div>
      </div>

      {error && (
        <div className="alert alert-danger mb-4">
          <FaExclamationTriangle className="me-2" />
          {error}
        </div>
      )}

      {/* Stats Row */}
      <Row className="mb-4 g-3">
        <Col lg={3} md={6}>
          <StatCard
            title="Tổng xe hiện tại"
            value={loading ? '...' : stats.occupiedSlots}
            trend={dailyTrends.vehicles}
            trendData={getVehicleTrendData()}
            color="#0d6efd"
            loading={loading}
            icon={<FaCarAlt size={20} />}
          />
        </Col>
        <Col lg={3} md={6}>
          <StatCard
            title="Xe đã rời đi hôm nay"
            value={loading ? '...' : exitedVehiclesCount}
            trend={dailyTrends.exits}
            trendData={getExitsTrendData()}
            color="#dc3545"
            loading={loading}
            icon={<FaChartBar size={20} />}
          />
        </Col>
        <Col lg={3} md={6}>
          <StatCard
            title="Doanh thu hôm nay"
            value={loading ? '...' : `${(stats.revenue).toLocaleString()} đ`}
            trend={dailyTrends.revenue}
            trendData={getRevenueTrendData()}
            color="#198754"
            loading={loading}
            icon={<FaMoneyBillWave size={20} />}
          />
        </Col>
        <Col lg={3} md={6}>
          <StatCard
            title="Xe đăng ký tháng"
            value={loading ? '...' : stats.monthlyVehicles}
            trend={dailyTrends.monthly}
            trendData={getMonthlyTrendData()}
            color="#6f42c1"
            loading={loading}
            icon={<FaUserCheck size={20} />}
          />
        </Col>
      </Row>

      {/* Activity Chart and Parking Status */}
      <Row className="mb-4">
        <Col md={8}>
          <ActivityChart
            title="Hoạt động xe vào/ra"
            data={activityData}
            loading={loading}
            onDateRangeChange={handleDateRangeChange}
            dateRange={dateRange}
          />
        </Col>
        <Col md={4}>
          <ParkingSpaceStatus
            title="Trạng thái bãi đỗ xe"
            occupancyRate={Math.round((stats.occupiedSlots / stats.totalSlots) * 100) || 0}
            availableSpaces={stats.availableSlots}
            occupiedSpaces={stats.occupiedSlots}
            reservedSpaces={stats.reservedSlots}
            totalSpaces={stats.totalSlots}
            carSpaces={stats.carSlots}
            motorcycleSpaces={stats.motorcycleSlots}
            loading={loading}
          />
        </Col>
      </Row>

      {/* Parking Lot Visualizer */}
      <Row className="mb-4">
        <Col md={12}>
          {loading ? (
            <Card className="dashboard-card">
              <Card.Body className="text-center py-5">
                <div className="spinner-border text-primary" role="status">
                  <span className="visually-hidden">Đang tải...</span>
                </div>
                <p className="mt-2">Đang tải sơ đồ bãi đỗ xe...</p>
              </Card.Body>
            </Card>
          ) : (
            <ParkingLotVisualizer slots={parkingSlots} vehicles={vehiclesMap} />
          )}
        </Col>
      </Row>

      {/* Recent Vehicles */}
      <Row>
        <Col md={12}>
          {loading ? (
            <Card className="dashboard-card">
              <Card.Header>Xe gần đây</Card.Header>
              <Card.Body className="text-center py-5">
                <div className="spinner-border text-primary" role="status">
                  <span className="visually-hidden">Đang tải...</span>
                </div>
                <p className="mt-2">Đang tải danh sách xe...</p>
              </Card.Body>
            </Card>
          ) : (
            <RecentVehicles vehicles={recentVehicles} />
          )}
        </Col>
      </Row>
    </div>
  );
};

export default Dashboard;
