import React, { useState, useEffect } from 'react';
import type { Device, FaultDiagnosisResult } from '@/types';
import { deviceApi, faultDiagnosisApi } from '@/services/api';
import { DeviceStatus, FaultSeverity } from '@/types';

interface FaultDiagnosisPanelProps {
  maxHistoryItems?: number;
}

const getConfidenceColor = (confidence: number) => {
  if (confidence >= 90) return { bg: 'bg-red-500', text: 'text-red-400', light: 'bg-red-500/20' };
  if (confidence >= 70) return { bg: 'bg-orange-500', text: 'text-orange-400', light: 'bg-orange-500/20' };
  return { bg: 'bg-yellow-500', text: 'text-yellow-400', light: 'bg-yellow-500/20' };
};

const getSeverityColor = (severity: number) => {
  switch (severity) {
    case FaultSeverity.Severe:
      return { bg: 'bg-red-500/20', border: 'border-red-500/30', text: 'text-red-400', label: '严重' };
    case FaultSeverity.Moderate:
      return { bg: 'bg-orange-500/20', border: 'border-orange-500/30', text: 'text-orange-400', label: '中等' };
    case FaultSeverity.Minor:
      return { bg: 'bg-yellow-500/20', border: 'border-yellow-500/30', text: 'text-yellow-400', label: '轻微' };
    default:
      return { bg: 'bg-slate-500/20', border: 'border-slate-500/30', text: 'text-slate-400', label: '未知' };
  }
};

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

export const FaultDiagnosisPanel: React.FC<FaultDiagnosisPanelProps> = ({ maxHistoryItems = 10 }) => {
  const [devices, setDevices] = useState<Device[]>([]);
  const [selectedDevice, setSelectedDevice] = useState<Device | null>(null);
  const [diagnosisResults, setDiagnosisResults] = useState<FaultDiagnosisResult[]>([]);
  const [historyResults, setHistoryResults] = useState<FaultDiagnosisResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [diagnosing, setDiagnosing] = useState(false);
  const [selectedResult, setSelectedResult] = useState<FaultDiagnosisResult | null>(null);

  useEffect(() => {
    const fetchDevices = async () => {
      try {
        const res = await deviceApi.getAll();
        setDevices(res.data);
        if (res.data.length > 0 && !selectedDevice) {
          setSelectedDevice(res.data[0]);
        }
      } catch (error) {
        console.error('获取设备列表失败:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchDevices();
  }, []);

  useEffect(() => {
    if (!selectedDevice) return;

    const fetchDiagnosisResults = async () => {
      try {
        setLoading(true);
        const res = await faultDiagnosisApi.getResults(selectedDevice.id, 1, 100);
        const confirmed = res.data.filter((r) => r.isConfirmed).slice(0, maxHistoryItems);
        const latest = res.data.filter((r) => !r.isConfirmed);
        setDiagnosisResults(latest);
        setHistoryResults(confirmed);
      } catch (error) {
        console.error('获取诊断结果失败:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchDiagnosisResults();
  }, [selectedDevice, maxHistoryItems]);

  const handleDiagnose = async () => {
    if (!selectedDevice) return;

    try {
      setDiagnosing(true);
      const res = await faultDiagnosisApi.diagnoseDevice(selectedDevice.id);
      setDiagnosisResults(res.data);
      setSelectedResult(null);
    } catch (error) {
      console.error('故障诊断失败:', error);
    } finally {
      setDiagnosing(false);
    }
  };

  const handleConfirm = async (resultId: number) => {
    try {
      await faultDiagnosisApi.confirmResult(resultId, 'admin');
      setDiagnosisResults((prev) => prev.filter((r) => r.id !== resultId));
      setSelectedResult(null);
      const updated = diagnosisResults.find((r) => r.id === resultId);
      if (updated) {
        setHistoryResults((prev) => [{ ...updated, isConfirmed: true, confirmedAt: new Date().toISOString() }, ...prev].slice(0, maxHistoryItems));
      }
    } catch (error) {
      console.error('确认诊断失败:', error);
    }
  };

  const formatTime = (timeStr: string) => {
    return new Date(timeStr).toLocaleString('zh-CN', {
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
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
          disabled={!selectedDevice || diagnosing}
          className="px-3 py-1.5 bg-blue-500 hover:bg-blue-600 disabled:bg-slate-600 disabled:cursor-not-allowed text-white text-sm rounded-lg transition-colors"
        >
          {diagnosing ? '诊断中...' : '手动诊断'}
        </button>
      </div>

      <div className="flex gap-4 h-[calc(100%-50px)]">
        <div className="w-64 flex-shrink-0 border-r border-slate-700 pr-4">
          <div className="text-slate-400 text-xs mb-2 font-medium">设备列表</div>
          <div className="space-y-2 overflow-y-auto max-h-full">
            {devices.map((device) => (
              <div
                key={device.id}
                onClick={() => setSelectedDevice(device)}
                className={`p-3 rounded-lg cursor-pointer transition-all ${
                  selectedDevice?.id === device.id
                    ? 'bg-blue-500/20 border border-blue-500/50'
                    : 'bg-slate-700/30 border border-transparent hover:bg-slate-700/50'
                }`}
              >
                <div className="flex items-center gap-2 mb-1">
                  <span className={`w-2 h-2 rounded-full ${getStatusColor(device.status)}`} />
                  <span className="text-white text-sm font-medium truncate">{device.name}</span>
                </div>
                <div className="text-xs text-slate-400">{device.deviceTypeName}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="flex-1 flex flex-col overflow-hidden">
          {selectedDevice ? (
            <>
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
                      {['待机', '运行', '故障', '维护'][selectedDevice.status] || '未知'}
                    </div>
                  </div>
                </div>
              </div>

              <div className="flex-1 grid grid-cols-2 gap-4 overflow-hidden">
                <div className="flex flex-col overflow-hidden">
                  <div className="text-slate-400 text-xs mb-2 font-medium">
                    当前诊断结果 ({diagnosisResults.length})
                  </div>
                  <div className="flex-1 overflow-y-auto space-y-2 pr-2">
                    {diagnosisResults.length === 0 ? (
                      <div className="text-center py-8 text-slate-400 text-sm">
                        暂无未确认的诊断结果
                      </div>
                    ) : (
                      diagnosisResults.map((result) => {
                        const confidenceColor = getConfidenceColor(result.confidence * 100);
                        const severityColor = result.faultType
                          ? getSeverityColor(result.faultType.severity)
                          : getSeverityColor(0);

                        return (
                          <div
                            key={result.id}
                            onClick={() => setSelectedResult(result)}
                            className={`p-3 rounded-lg cursor-pointer transition-all ${
                              selectedResult?.id === result.id
                                ? `${severityColor.bg} ${severityColor.border} border`
                                : 'bg-slate-700/30 border border-transparent hover:bg-slate-700/50'
                            }`}
                          >
                            <div className="flex items-start justify-between gap-2 mb-2">
                              <div className="flex-1 min-w-0">
                                <div className="text-white text-sm font-medium truncate">
                                  {result.faultType?.faultName || '未知故障'}
                                </div>
                                <div className="text-xs text-slate-400 mt-0.5">
                                  {formatTime(result.timestamp)}
                                </div>
                              </div>
                              {result.faultType && (
                                <span className={`text-xs px-2 py-0.5 rounded-full ${severityColor.bg} ${severityColor.text} shrink-0`}>
                                  {severityColor.label}
                                </span>
                              )}
                            </div>

                            <div className="space-y-1.5">
                              <div className="flex items-center gap-2">
                                <span className="text-xs text-slate-400 w-12">置信度</span>
                                <div className="flex-1 h-1.5 bg-slate-600 rounded-full overflow-hidden">
                                  <div
                                    className={`h-full ${confidenceColor.bg} transition-all`}
                                    style={{ width: `${result.confidence * 100}%` }}
                                  />
                                </div>
                                <span className={`text-xs font-medium ${confidenceColor.text} w-10 text-right`}>
                                  {(result.confidence * 100).toFixed(0)}%
                                </span>
                              </div>
                              <div className="flex items-center gap-2">
                                <span className="text-xs text-slate-400 w-12">贝叶斯</span>
                                <span className="text-xs text-cyan-400">
                                  {(result.bayesProbability * 100).toFixed(1)}%
                                </span>
                              </div>
                            </div>
                          </div>
                        );
                      })
                    )}
                  </div>
                </div>

                <div className="flex flex-col overflow-hidden">
                  <div className="text-slate-400 text-xs mb-2 font-medium">详细信息</div>
                  <div className="flex-1 overflow-y-auto pr-2">
                    {selectedResult ? (
                      <div className="space-y-3">
                        <div className={`p-3 rounded-lg ${getConfidenceColor(selectedResult.confidence * 100).light} border border-opacity-30`}>
                          <div className="text-white font-medium mb-1">
                            {selectedResult.faultType?.faultName || '未知故障'}
                          </div>
                          <div className="text-xs text-slate-300">
                            {selectedResult.faultType?.description || '暂无描述'}
                          </div>
                        </div>

                        {selectedResult.matchingSymptoms && (
                          <div className="p-3 bg-slate-700/30 rounded-lg">
                            <div className="text-xs text-slate-400 mb-1">匹配症状</div>
                            <div className="text-sm text-white">{selectedResult.matchingSymptoms}</div>
                          </div>
                        )}

                        {selectedResult.deviationDetails && (
                          <div className="p-3 bg-slate-700/30 rounded-lg">
                            <div className="text-xs text-slate-400 mb-1">参数偏离详情</div>
                            <div className="text-sm text-white whitespace-pre-wrap">{selectedResult.deviationDetails}</div>
                          </div>
                        )}

                        {selectedResult.maintenanceRecommendation && (
                          <div className="p-3 bg-emerald-500/10 border border-emerald-500/30 rounded-lg">
                            <div className="flex items-center gap-2 mb-1">
                              <span className="text-emerald-400">🔧</span>
                              <span className="text-xs text-emerald-400 font-medium">检修建议</span>
                            </div>
                            <div className="text-sm text-white">{selectedResult.maintenanceRecommendation}</div>
                          </div>
                        )}

                        {selectedResult.faultType?.typicalCauses && (
                          <div className="p-3 bg-slate-700/30 rounded-lg">
                            <div className="text-xs text-slate-400 mb-1">常见原因</div>
                            <div className="text-sm text-white">{selectedResult.faultType.typicalCauses}</div>
                          </div>
                        )}

                        {!selectedResult.isConfirmed && (
                          <button
                            onClick={() => handleConfirm(selectedResult.id)}
                            className="w-full py-2 bg-green-500 hover:bg-green-600 text-white text-sm rounded-lg transition-colors"
                          >
                            确认诊断
                          </button>
                        )}
                      </div>
                    ) : (
                      <div className="text-center py-12 text-slate-400 text-sm">
                        选择一个诊断结果查看详情
                      </div>
                    )}
                  </div>
                </div>
              </div>

              <div className="mt-4 border-t border-slate-700 pt-4">
                <div className="text-slate-400 text-xs mb-2 font-medium">
                  诊断历史记录 ({historyResults.length})
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="text-slate-400 text-xs">
                        <th className="text-left pb-2 font-medium">故障名称</th>
                        <th className="text-left pb-2 font-medium">置信度</th>
                        <th className="text-left pb-2 font-medium">严重程度</th>
                        <th className="text-left pb-2 font-medium">诊断时间</th>
                        <th className="text-left pb-2 font-medium">确认时间</th>
                      </tr>
                    </thead>
                    <tbody>
                      {historyResults.length === 0 ? (
                        <tr>
                          <td colSpan={5} className="text-center py-4 text-slate-400">
                            暂无历史记录
                          </td>
                        </tr>
                      ) : (
                        historyResults.map((result) => {
                          const severityColor = result.faultType
                            ? getSeverityColor(result.faultType.severity)
                            : getSeverityColor(0);

                          return (
                            <tr key={result.id} className="border-t border-slate-700/50">
                              <td className="py-2 text-white">
                                {result.faultType?.faultName || '未知故障'}
                              </td>
                              <td className="py-2">
                                <span className={getConfidenceColor(result.confidence * 100).text}>
                                  {(result.confidence * 100).toFixed(0)}%
                                </span>
                              </td>
                              <td className="py-2">
                                <span className={`text-xs px-2 py-0.5 rounded-full ${severityColor.bg} ${severityColor.text}`}>
                                  {severityColor.label}
                                </span>
                              </td>
                              <td className="py-2 text-slate-400">
                                {formatTime(result.timestamp)}
                              </td>
                              <td className="py-2 text-slate-400">
                                {result.confirmedAt ? formatTime(result.confirmedAt) : '-'}
                              </td>
                            </tr>
                          );
                        })
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </>
          ) : (
            <div className="flex items-center justify-center h-full text-slate-400">
              请选择一个设备
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
