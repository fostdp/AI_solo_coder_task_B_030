import React from 'react';
import type { FaultDiagnosisResult } from '@/types';
import { FaultSeverity } from '@/types';

interface HistoryTableProps {
  historyResults: FaultDiagnosisResult[];
}

const getConfidenceColor = (confidence: number) => {
  if (confidence >= 90) return 'text-red-400';
  if (confidence >= 70) return 'text-orange-400';
  return 'text-yellow-400';
};

const getSeverityColor = (severity: number) => {
  switch (severity) {
    case FaultSeverity.Severe:
      return { bg: 'bg-red-500/20', text: 'text-red-400', label: '严重' };
    case FaultSeverity.Moderate:
      return { bg: 'bg-orange-500/20', text: 'text-orange-400', label: '中等' };
    case FaultSeverity.Minor:
      return { bg: 'bg-yellow-500/20', text: 'text-yellow-400', label: '轻微' };
    default:
      return { bg: 'bg-slate-500/20', text: 'text-slate-400', label: '未知' };
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

export const HistoryTable: React.FC<HistoryTableProps> = ({ historyResults }) => {
  return (
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
                      <span className={getConfidenceColor(result.confidence * 100)}>
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
  );
};
