import React from 'react';
import type { BenchmarkReport } from '@/types';

interface ReportGeneratorProps {
  report: BenchmarkReport | null;
  onClose: () => void;
}

export const ReportGenerator: React.FC<ReportGeneratorProps> = ({ report, onClose }) => {
  if (!report) return null;

  return (
    <div className="mt-4 p-4 bg-emerald-500/10 border border-emerald-500/30 rounded-lg">
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-center gap-2">
          <span className="text-emerald-400">✅</span>
          <span className="text-emerald-400 font-medium">报告生成成功</span>
        </div>
        <button
          onClick={onClose}
          className="text-slate-400 hover:text-white text-sm"
        >
          ✕
        </button>
      </div>
      <div className="text-sm text-slate-300 space-y-1">
        <div>报告名称: <span className="text-white">{report.reportName}</span></div>
        <div>报告周期: <span className="text-white">{report.reportPeriod}</span></div>
        <div>综合评分: <span className="text-emerald-400 font-bold">{report.overallScore?.toFixed(1) || '-'}</span></div>
        {report.bestPractices && (
          <div className="mt-2 pt-2 border-t border-slate-600/50">
            <span className="text-slate-400">最佳实践: </span>
            <span className="text-white">{report.bestPractices}</span>
          </div>
        )}
        {report.improvementSuggestions && (
          <div className="mt-2">
            <span className="text-slate-400">改进建议: </span>
            <span className="text-white">{report.improvementSuggestions}</span>
          </div>
        )}
      </div>
    </div>
  );
};
