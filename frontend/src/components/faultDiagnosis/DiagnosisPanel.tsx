import React from 'react';
import type { Device } from '@/types';
import { DeviceStatus } from '@/types';

interface DiagnosisPanelProps {
  selectedDevice: Device | null;
  children: React.ReactNode;
}

const getStatusColor = (status: number) => {
  switch (status) {
    case DeviceStatus.Running:
      return 'bg-green-500';
    case DeviceStatus.Standby:
      return 'bg-blue-500';
    case DeviceStatus.Fault:
      return 'bg-red-500';
    case DeviceStatus.Maintenance:
      return 'bg-yellow-500';
    default:
      return 'bg-slate-500';
  }
};

const getStatusText = (status: number) => {
  return ['待机', '运行', '故障', '维护'][status] || '未知';
};

export const DiagnosisPanel: React.FC<DiagnosisPanelProps> = ({
  selectedDevice,
  children,
}) => {
  if (!selectedDevice) {
    return (
      <div className="flex items-center justify-center h-full text-slate-400">
        请选择一个设备
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="mb-4 p-3 bg-slate-700/30 rounded-lg">
        <div className="flex items-center justify-between">
          <div>
            <div className="text-white font-medium">{selectedDevice.name}</div>
            <div className="text-xs text-slate-400 mt-1">
              设计COP: {selectedDevice.designCOP} · 额定功率: {selectedDevice.ratedPower}kW
            </div>
          </div>
          <div className="text-right">
            <div className={`text-xs px-2 py-1 rounded-full ${getStatusColor(selectedDevice.status)} bg-opacity-20 text-white`}>
              {getStatusText(selectedDevice.status)}
            </div>
          </div>
        </div>
      </div>

      {children}
    </div>
  );
};
