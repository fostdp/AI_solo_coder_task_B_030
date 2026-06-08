import React, { useState, useEffect } from 'react';
import { useFaultDiagnosis } from '@/hooks/useFaultDiagnosis';
import { DeviceSelector } from './faultDiagnosis/DeviceSelector';
import { DiagnosisPanel } from './faultDiagnosis/DiagnosisPanel';
import { ResultCard } from './faultDiagnosis/ResultCard';
import { HistoryTable } from './faultDiagnosis/HistoryTable';

interface FaultDiagnosisPanelProps {
  maxHistoryItems?: number;
}

export const FaultDiagnosisPanel: React.FC<FaultDiagnosisPanelProps> = ({ maxHistoryItems = 10 }) => {
  const [selectedDevice, setSelectedDevice] = useState<any>(null);
  const [selectedResult, setSelectedResult] = useState<any>(null);
  const {
    devices,
    diagnosisResults,
    historyResults,
    loading,
    isDiagnosing,
    fetchDevices,
    fetchDiagnosisResults,
    diagnoseDevice,
    confirmDiagnosis,
  } = useFaultDiagnosis();

  useEffect(() => {
    const initDevices = async () => {
      const devicesData = await fetchDevices();
      if (devicesData && devicesData.length > 0 && !selectedDevice) {
        setSelectedDevice(devicesData[0]);
      }
    };
    initDevices();
  }, []);

  useEffect(() => {
    if (!selectedDevice) return;
    fetchDiagnosisResults(selectedDevice.id, maxHistoryItems);
  }, [selectedDevice, maxHistoryItems, fetchDiagnosisResults]);

  const handleDiagnose = async () => {
    if (!selectedDevice) return;
    await diagnoseDevice(selectedDevice.id);
    setSelectedResult(null);
  };

  const handleConfirm = async (resultId: number) => {
    await confirmDiagnosis(resultId, 'admin');
    setSelectedResult(null);
  };

  const handleDeviceSelect = (device: any) => {
    setSelectedDevice(device);
    setSelectedResult(null);
  };

  if (loading && !devices.length) {
    return (
      <div className="bg-slate-800/50 border border-slate-700 rounded-xl p-4 h-full">
        <h3 className="text-white font-semibold mb-4">故障诊断面板</h3>
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin w-6 h-6 border-2 border-blue-500 border-t-transparent rounded-full" />
        </div>
      </div>
    );
  }

  return (
    <div className="bg-slate-800/50 border border-slate-700 rounded-xl p-4 h-full">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-white font-semibold">故障诊断面板</h3>
        <button
          onClick={handleDiagnose}
          disabled={!selectedDevice || isDiagnosing}
          className="px-3 py-1.5 bg-blue-500 hover:bg-blue-600 disabled:bg-slate-600 disabled:cursor-not-allowed text-white text-sm rounded-lg transition-colors"
        >
          {isDiagnosing ? '诊断中...' : '手动诊断'}
        </button>
      </div>

      <div className="flex gap-4 h-[calc(100%-50px)]">
        <DeviceSelector
          devices={devices}
          selectedDevice={selectedDevice}
          onDeviceSelect={handleDeviceSelect}
        />

        <DiagnosisPanel selectedDevice={selectedDevice}>
          <ResultCard
            results={diagnosisResults}
            selectedResult={selectedResult}
            onResultSelect={setSelectedResult}
            onConfirm={handleConfirm}
          />

          <HistoryTable historyResults={historyResults} />
        </DiagnosisPanel>
      </div>
    </div>
  );
};
