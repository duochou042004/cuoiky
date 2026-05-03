/// <reference path="../vite-env.d.ts" />
import React, { useState, useEffect, useMemo } from 'react';
import { Card, Badge, Nav, Form, InputGroup, Button, Tooltip, OverlayTrigger, Row, Col } from 'react-bootstrap';
import { FaCar, FaMotorcycle, FaSearch, FaFilter, FaClipboardList, FaMapMarkedAlt, FaAngleRight, FaTimes } from 'react-icons/fa';
import '../styles/parking-visualizer.css';

interface ParkingSlot {
  slotId: string;
  status: string;
  type: string;
  currentVehicleId: string | null;
}

interface VehicleInfo {
  licensePlate?: string;
  entryTime?: string;
  vehicleType?: string;
  isMonthlyRegistered?: boolean;
}

// Thêm interfaces mới để xác định kiểu cho cấu trúc dữ liệu
interface Zone {
  name: string;
  rows: number;
  cols: number;
  startId: number;
}

interface ZoneWithSlots {
  name: string;
  slots: ParkingSlot[][];
}

interface OrganizedSlots {
  MOTORBIKE: ZoneWithSlots[];
  CAR: ZoneWithSlots[];
}

interface ParkingLotVisualizerProps {
  slots: ParkingSlot[];
  vehicles?: Record<string, VehicleInfo>;
}

type ViewMode = 'grid' | 'realistic';
type SortOption = 'id' | 'status';

const ParkingLotVisualizer: React.FC<ParkingLotVisualizerProps> = ({ slots, vehicles = {} }) => {
  const [activeType, setActiveType] = useState<string>('ALL');
  const [searchTerm, setSearchTerm] = useState<string>('');
  const [viewMode, setViewMode] = useState<ViewMode>('realistic');
  const [sortBy, setSortBy] = useState<SortOption>('id');
  const [selectedSlot, setSelectedSlot] = useState<string | null>(null);
  const [showFilters, setShowFilters] = useState<boolean>(false);
  const [statusFilter, setStatusFilter] = useState<string>('ALL');

  // Define zones for realistic view
  const zoneLayout: Record<string, Zone[]> = {
    MOTORBIKE: [
      { name: 'Khu A', rows: 5, cols: 6, startId: 1 },
      { name: 'Khu B', rows: 5, cols: 6, startId: 31 },
      { name: 'Khu C', rows: 5, cols: 6, startId: 61 },
      { name: 'Khu D', rows: 5, cols: 6, startId: 91 },
    ],
    CAR: [
      { name: 'Khu E', rows: 3, cols: 5, startId: 1 },
      { name: 'Khu F', rows: 3, cols: 5, startId: 16 },
      { name: 'Khu G', rows: 2, cols: 5, startId: 31 },
    ]
  };

  // Process and filter slots
  const processedSlots = useMemo(() => {
    let result = [...slots];

    // Apply type filter
    if (activeType !== 'ALL') {
      result = result.filter(slot => slot.type === activeType);
    }

    // Apply status filter
    if (statusFilter !== 'ALL') {
      result = result.filter(slot => slot.status === statusFilter);
    }

    // Apply search filter
    if (searchTerm) {
      result = result.filter(slot => 
        slot.slotId.toLowerCase().includes(searchTerm.toLowerCase()) ||
        (slot.currentVehicleId && slot.currentVehicleId.toLowerCase().includes(searchTerm.toLowerCase())) ||
        (slot.currentVehicleId && vehicles[slot.currentVehicleId]?.licensePlate?.toLowerCase().includes(searchTerm.toLowerCase()))
      );
    }

    // Apply sorting
    if (sortBy === 'id') {
      result.sort((a, b) => a.slotId.localeCompare(b.slotId, undefined, { numeric: true }));
    } else if (sortBy === 'status') {
      result.sort((a, b) => {
        const statusOrder = { OCCUPIED: 1, RESERVED: 2, AVAILABLE: 3 };
        return statusOrder[a.status as keyof typeof statusOrder] - statusOrder[b.status as keyof typeof statusOrder];
      });
    }

    return result;
  }, [slots, activeType, searchTerm, sortBy, statusFilter, vehicles]);

  // Group slots by type for statistics
  const slotStats = useMemo(() => {
    const motorbikeSlots = slots.filter(slot => slot.type === 'MOTORBIKE');
    const carSlots = slots.filter(slot => slot.type === 'CAR');

    return {
      MOTORBIKE: {
        total: motorbikeSlots.length,
        available: motorbikeSlots.filter(slot => slot.status === 'AVAILABLE').length,
        occupied: motorbikeSlots.filter(slot => slot.status === 'OCCUPIED').length,
        reserved: motorbikeSlots.filter(slot => slot.status === 'RESERVED').length,
      },
      CAR: {
        total: carSlots.length,
        available: carSlots.filter(slot => slot.status === 'AVAILABLE').length,
        occupied: carSlots.filter(slot => slot.status === 'OCCUPIED').length,
        reserved: carSlots.filter(slot => slot.status === 'RESERVED').length,
      }
    };
  }, [slots]);

  const lotHealth = useMemo(() => {
    const total = slots.length || 1;
    const occupied = slots.filter(slot => slot.status === 'OCCUPIED').length;
    const reserved = slots.filter(slot => slot.status === 'RESERVED').length;
    const available = slots.filter(slot => slot.status === 'AVAILABLE').length;

    return {
      occupancy: Math.round((occupied / total) * 100),
      available,
      occupied,
      reserved,
      total
    };
  }, [slots]);

  // Get details for a specific slot ID
  const getSlotDetails = (slotId: string) => {
    const slot = slots.find(s => s.slotId === slotId);
    if (!slot) return null;

    const vehicleInfo = slot.currentVehicleId ? vehicles[slot.currentVehicleId] : null;
    
    return {
      ...slot,
      vehicleInfo
    };
  };

  // Organize slots for the realistic view
  const organizedSlots = useMemo<OrganizedSlots>(() => {
    const result: OrganizedSlots = { MOTORBIKE: [], CAR: [] };
    
    // For motorcycle zones
    zoneLayout.MOTORBIKE.forEach(zone => {
      const zoneSlots: ParkingSlot[][] = [];
      for (let row = 0; row < zone.rows; row++) {
        const rowSlots: ParkingSlot[] = [];
        for (let col = 0; col < zone.cols; col++) {
          const slotNumber = zone.startId + row * zone.cols + col;
          const slotId = `M${slotNumber.toString().padStart(3, '0')}`;
          const slot = slots.find(s => s.slotId === slotId) || {
            slotId,
            status: 'UNKNOWN',
            type: 'MOTORBIKE',
            currentVehicleId: null
          };
          rowSlots.push(slot);
        }
        zoneSlots.push(rowSlots);
      }
      result.MOTORBIKE.push({ name: zone.name, slots: zoneSlots });
    });
    
    // For car zones
    zoneLayout.CAR.forEach(zone => {
      const zoneSlots: ParkingSlot[][] = [];
      for (let row = 0; row < zone.rows; row++) {
        const rowSlots: ParkingSlot[] = [];
        for (let col = 0; col < zone.cols; col++) {
          const slotNumber = zone.startId + row * zone.cols + col;
          const slotId = `C${slotNumber.toString().padStart(3, '0')}`;
          const slot = slots.find(s => s.slotId === slotId) || {
            slotId,
            status: 'UNKNOWN',
            type: 'CAR',
            currentVehicleId: null
          };
          rowSlots.push(slot);
        }
        zoneSlots.push(rowSlots);
      }
      result.CAR.push({ name: zone.name, slots: zoneSlots });
    });
    
    return result;
  }, [slots]);

  // Render tooltip content for a slot
  const renderTooltip = (slot: ParkingSlot) => (
    <Tooltip id={`tooltip-${slot.slotId}`} className="parking-tooltip">
      <div className="slot-tooltip">
        <h6>{slot.slotId}</h6>
        <div><strong>Trạng thái:</strong> {renderStatus(slot.status)}</div>
        {slot.currentVehicleId && vehicles[slot.currentVehicleId] && (
          <>
            <div><strong>Biển số:</strong> {vehicles[slot.currentVehicleId].licensePlate}</div>
            <div><strong>Thời gian vào:</strong> {
              new Date(vehicles[slot.currentVehicleId].entryTime || '').toLocaleString('vi-VN')
            }</div>
            <div>
              <strong>Loại:</strong> {vehicles[slot.currentVehicleId].isMonthlyRegistered ? 
                'Xe tháng' : 'Xe thường'
              }
            </div>
          </>
        )}
      </div>
    </Tooltip>
  );

  // Render status badge
  const renderStatus = (status: string) => {
    switch (status) {
      case 'AVAILABLE':
        return <Badge bg="success">Trống</Badge>;
      case 'OCCUPIED':
        return <Badge bg="danger">Đã đỗ</Badge>;
      case 'RESERVED':
        return <Badge bg="warning" text="dark">Đã đặt</Badge>;
      default:
        return <Badge bg="secondary">Chưa xác định</Badge>;
    }
  };

  // Render slot in grid view
  const renderGridSlot = (slot: ParkingSlot) => {
    let slotClass = 'available';
    if (slot.status === 'OCCUPIED') {
      slotClass = 'occupied';
    } else if (slot.status === 'RESERVED') {
      slotClass = 'reserved';
    }

    return (
      <OverlayTrigger
        placement="top"
        overlay={renderTooltip(slot)}
        key={slot.slotId}
      >
        <div
          className={`parking-slot ${slotClass} ${selectedSlot === slot.slotId ? 'selected' : ''}`}
          onClick={() => setSelectedSlot(selectedSlot === slot.slotId ? null : slot.slotId)}
        >
          <div className="d-flex align-items-center justify-content-center">
            {slot.status === 'OCCUPIED' && (
              <span className="vehicle-icon me-1">
                {slot.type === 'CAR' ? <FaCar /> : <FaMotorcycle />}
              </span>
            )}
            {slot.status === 'RESERVED' && (
              <span className="vehicle-icon me-1 reserved">
                {slot.type === 'CAR' ? <FaCar /> : <FaMotorcycle />}
              </span>
            )}
            <span>{slot.slotId}</span>
          </div>
        </div>
      </OverlayTrigger>
    );
  };

  // Render slot in realistic view
  const renderRealisticSlot = (slot: ParkingSlot) => {
    let slotClass = 'available';
    if (slot.status === 'OCCUPIED') {
      slotClass = 'occupied';
    } else if (slot.status === 'RESERVED') {
      slotClass = 'reserved';
    }

    return (
      <OverlayTrigger
        placement="top"
        overlay={renderTooltip(slot)}
        key={slot.slotId}
      >
        <div
          className={`realistic-slot ${slotClass} ${slot.type.toLowerCase()} ${selectedSlot === slot.slotId ? 'selected' : ''}`}
          onClick={() => setSelectedSlot(selectedSlot === slot.slotId ? null : slot.slotId)}
        >
          <div className="slot-content">
            {slot.status === 'OCCUPIED' && (
              <div className="vehicle-icon">
                {slot.type === 'CAR' ? <FaCar size={18} /> : <FaMotorcycle size={16} />}
              </div>
            )}
            {slot.status === 'RESERVED' && (
              <div className="vehicle-icon reserved">
                {slot.type === 'CAR' ? <FaCar size={18} /> : <FaMotorcycle size={16} />}
              </div>
            )}
            <div className="slot-id">{slot.slotId}</div>
          </div>
        </div>
      </OverlayTrigger>
    );
  };

  return (
    <Card className="parking-visualizer-card parking-visualizer-2026">
      <Card.Header className="visualizer-header-2026">
        <div className="visualizer-orb" aria-hidden="true"></div>
        <div className="d-flex justify-content-between align-items-center flex-wrap">
          <div className="d-flex align-items-center">
            <div>
              <div className="visualizer-kicker">Parking Experience 2026</div>
              <h5 className="mb-0">Sơ đồ bãi đỗ xe thông minh</h5>
            </div>
            <div className="ms-3">
              <Button
                size="sm"
                variant={viewMode === 'grid' ? 'primary' : 'outline-primary'}
                className="me-2"
                onClick={() => setViewMode('grid')}
              >
                <FaClipboardList className="me-1" />
                <span className="d-none d-sm-inline">Lưới</span>
              </Button>
              <Button
                size="sm"
                variant={viewMode === 'realistic' ? 'primary' : 'outline-primary'}
                onClick={() => setViewMode('realistic')}
              >
                <FaMapMarkedAlt className="me-1" />
                <span className="d-none d-sm-inline">Sơ đồ</span>
              </Button>
            </div>
          </div>

          <div className="d-flex align-items-center mt-2 mt-md-0">
            <InputGroup className="filter-input-group">
              <InputGroup.Text>
                <FaSearch />
              </InputGroup.Text>
              <Form.Control
                placeholder="Tìm kiếm vị trí..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
              />
              <Button 
                variant="outline-secondary"
                onClick={() => setShowFilters(!showFilters)}
                title="Lọc"
              >
                <FaFilter />
              </Button>
            </InputGroup>
          </div>
        </div>

        <div className="lot-health-strip mt-3">
          <div className="lot-health-card accent-blue">
            <span>Tỷ lệ lấp đầy</span>
            <strong>{lotHealth.occupancy}%</strong>
          </div>
          <div className="lot-health-card accent-green">
            <span>Còn trống</span>
            <strong>{lotHealth.available}</strong>
          </div>
          <div className="lot-health-card accent-red">
            <span>Đang đỗ</span>
            <strong>{lotHealth.occupied}</strong>
          </div>
          <div className="lot-health-card accent-amber">
            <span>Xe tháng</span>
            <strong>{lotHealth.reserved}</strong>
          </div>
        </div>

        {showFilters && (
          <div className="filter-panel mt-3 p-2 border rounded">
            <Row className="g-2">
              <Col md={3} sm={6}>
                <Form.Group>
                  <Form.Label className="mb-0"><small>Loại xe</small></Form.Label>
                  <Form.Select
                    size="sm"
                    value={activeType}
                    onChange={(e) => setActiveType(e.target.value)}
                  >
                    <option value="ALL">Tất cả</option>
                    <option value="MOTORBIKE">Xe máy</option>
                    <option value="CAR">Ô tô</option>
                  </Form.Select>
                </Form.Group>
              </Col>
              <Col md={3} sm={6}>
                <Form.Group>
                  <Form.Label className="mb-0"><small>Trạng thái</small></Form.Label>
                  <Form.Select
                    size="sm"
                    value={statusFilter}
                    onChange={(e) => setStatusFilter(e.target.value)}
                  >
                    <option value="ALL">Tất cả</option>
                    <option value="AVAILABLE">Trống</option>
                    <option value="OCCUPIED">Đã đỗ</option>
                    <option value="RESERVED">Đã đặt</option>
                  </Form.Select>
                </Form.Group>
              </Col>
              <Col md={3} sm={6}>
                <Form.Group>
                  <Form.Label className="mb-0"><small>Sắp xếp theo</small></Form.Label>
                  <Form.Select
                    size="sm"
                    value={sortBy}
                    onChange={(e) => setSortBy(e.target.value as SortOption)}
                  >
                    <option value="id">Vị trí (ID)</option>
                    <option value="status">Trạng thái</option>
                  </Form.Select>
                </Form.Group>
              </Col>
              <Col md={3} sm={6} className="d-flex align-items-end">
                <Button 
                  variant="outline-danger" 
                  size="sm"
                  className="w-100"
                  onClick={() => {
                    setSearchTerm('');
                    setActiveType('ALL');
                    setStatusFilter('ALL');
                    setSortBy('id');
                  }}
                >
                  <FaTimes className="me-1" /> Xóa bộ lọc
                </Button>
              </Col>
            </Row>
          </div>
        )}
      </Card.Header>

      <Card.Body className="p-0 p-md-3">
        {/* Slot statistics */}
        <div className="slot-statistics mb-3 p-2 pt-3">
          <Row>
            <Col md={6} className="d-flex align-items-center">
              <div className="status-legend me-3">
                <span className="legend-item">
                  <span className="status-dot available"></span> Trống
                </span>
                <span className="legend-item">
                  <span className="status-dot occupied"></span> Đã đỗ
                </span>
                <span className="legend-item">
                  <span className="status-dot reserved"></span> Đã đặt
                </span>
              </div>
            </Col>
            <Col md={6}>
              <Row className="g-2">
                <Col xs={6}>
                  <div className="stats-card">
                    <div className="stats-title">
                      <FaMotorcycle /> Xe máy
                    </div>
                    <div className="stats-content">
                      <div className="stats-item available">
                        <strong>{slotStats.MOTORBIKE.available}</strong><span>Trống</span>
                      </div>
                      <div className="stats-item occupied">
                        <strong>{slotStats.MOTORBIKE.occupied}</strong><span>Đã đỗ</span>
                      </div>
                      <div className="stats-item reserved">
                        <strong>{slotStats.MOTORBIKE.reserved}</strong><span>Đã đặt</span>
                      </div>
                    </div>
                  </div>
                </Col>
                <Col xs={6}>
                  <div className="stats-card">
                    <div className="stats-title">
                      <FaCar /> Ô tô
                    </div>
                    <div className="stats-content">
                      <div className="stats-item available">
                        <strong>{slotStats.CAR.available}</strong><span>Trống</span>
                      </div>
                      <div className="stats-item occupied">
                        <strong>{slotStats.CAR.occupied}</strong><span>Đã đỗ</span>
                      </div>
                      <div className="stats-item reserved">
                        <strong>{slotStats.CAR.reserved}</strong><span>Đã đặt</span>
                      </div>
                    </div>
                  </div>
                </Col>
              </Row>
            </Col>
          </Row>
        </div>

        {/* Selected slot details */}
        {selectedSlot && (
          <div className="selected-slot-details mb-3">
            <Card className="border-primary">
              <Card.Header className="bg-primary text-white d-flex justify-content-between align-items-center">
                <h6 className="mb-0">Thông tin chi tiết: {selectedSlot}</h6>
                <Button 
                  variant="link" 
                  className="p-0 text-white" 
                  onClick={() => setSelectedSlot(null)}
                >
                  <FaTimes />
                </Button>
              </Card.Header>
              <Card.Body>
                {(() => {
                  const details = getSlotDetails(selectedSlot);
                  if (!details) return <p>Không tìm thấy thông tin</p>;

                  return (
                    <div>
                      <Row>
                        <Col md={6}>
                          <p><strong>Vị trí:</strong> {details.slotId}</p>
                          <p><strong>Loại:</strong> {details.type === 'CAR' ? 'Ô tô' : 'Xe máy'}</p>
                          <p><strong>Trạng thái:</strong> {renderStatus(details.status)}</p>
                        </Col>
                        {details.vehicleInfo && (
                          <Col md={6}>
                            <p><strong>Biển số:</strong> {details.vehicleInfo.licensePlate}</p>
                            <p><strong>Vào lúc:</strong> {
                              new Date(details.vehicleInfo.entryTime || '').toLocaleString('vi-VN')
                            }</p>
                            <p>
                              <strong>Loại đăng ký:</strong> {details.vehicleInfo.isMonthlyRegistered ? 
                                <Badge bg="primary">Xe tháng</Badge> : 
                                <Badge bg="secondary">Xe thường</Badge>
                              }
                            </p>
                          </Col>
                        )}
                      </Row>
                    </div>
                  );
                })()}
              </Card.Body>
            </Card>
          </div>
        )}

        {/* Grid View */}
        {viewMode === 'grid' && (
          <div className="parking-grid-container">
            {processedSlots.length > 0 ? (
              <div className="parking-grid">
                {processedSlots.map(slot => renderGridSlot(slot))}
              </div>
            ) : (
              <div className="text-center py-4">Không tìm thấy vị trí đỗ xe phù hợp</div>
            )}
          </div>
        )}

        {/* Realistic View */}
        {viewMode === 'realistic' && (
          <div className="realistic-view">
            {(activeType === 'ALL' || activeType === 'MOTORBIKE') && (
              <div className="parking-zone-container">
                <h5 className="zone-type-title">
                  <FaMotorcycle className="me-2" /> Khu vực xe máy
                </h5>

                {organizedSlots.MOTORBIKE.map((zone, zIndex) => (
                  <div className="parking-zone mb-4" key={`moto-zone-${zIndex}`}>
                    <h6 className="zone-name">{zone.name}</h6>
                    
                    {/* Main road */}
                    <div className="main-road"></div>
                    
                    {/* Parking slots */}
                    <div className="zone-slots">
                      {zone.slots.map((row, rowIndex) => (
                        <div className="slot-row" key={`moto-row-${zIndex}-${rowIndex}`}>
                          {row.map((slot, colIndex) => renderRealisticSlot(slot))}
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {(activeType === 'ALL' || activeType === 'CAR') && (
              <div className="parking-zone-container">
                <h5 className="zone-type-title mt-4">
                  <FaCar className="me-2" /> Khu vực ô tô
                </h5>

                {organizedSlots.CAR.map((zone, zIndex) => (
                  <div className="parking-zone mb-4" key={`car-zone-${zIndex}`}>
                    <h6 className="zone-name">{zone.name}</h6>
                    
                    {/* Main road */}
                    <div className="main-road"></div>
                    
                    {/* Parking slots */}
                    <div className="zone-slots">
                      {zone.slots.map((row, rowIndex) => (
                        <div className="slot-row" key={`car-row-${zIndex}-${rowIndex}`}>
                          {row.map((slot, colIndex) => renderRealisticSlot(slot))}
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {processedSlots.length === 0 && (
              <div className="text-center py-4">Không tìm thấy vị trí đỗ xe phù hợp</div>
            )}
          </div>
        )}
      </Card.Body>
    </Card>
  );
};

export default ParkingLotVisualizer;
