import React from 'react';
import type { FaultDiagnosisResult } from '@/types';
import { FaultSeverity } from '@/types';

interface ResultCardProps {
  results: FaultDiagnosisResult[];
  selectedResult: FaultDiagnosisResult | null;
  onResultSelect: (result: FaultDiagnosisResult) => void;
  onConfirm: (resultId: number) => void;
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

const formatTime = (timeStr: string) => {
  return new Date(timeStr).toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
};

export const ResultCard: React.FC<ResultCardProps> = ({
  results,
  selectedResult,
  onResultSelect,
  onConfirm,
}) => {
  return (
    <div className="flex-1 grid grid-cols-2 gap-4 overflow-hidden">
      <div className="flex flex-col overflow-hidden">
        <div className="text-slate-400 text-xs mb-2 font-medium">
          当前诊断结果 ({results.length})
        </div>
        <div className="flex-1 overflow-y-auto space-y-2 pr-2">
          {results.length === 0 ? (
            <div className="text-center py-8 text-slate-400 text-sm">
              暂无未确认的诊断结果
            </div>
          ) : (
            results.map((result) => {
              const confidenceColor = getConfidenceColor(result.confidence * 100);
              const severityColor = result.faultType
                ? getSeverityColor(result.faultType.severity)
                : getSeverityColor(0);

              return (
                <div
                  key={result.id}
                  onClick={() => onResultSelect(result)}
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
                  onClick={() => onConfirm(selectedResult.id)}
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
  );
};
