import { getCollectorStatusLabel } from '../model/collectorStatus';

interface CollectorStatusBadgeProps {
  status: string;
}

export function CollectorStatusBadge({ status }: CollectorStatusBadgeProps) {
  const label = getCollectorStatusLabel(status);

  return (
    <span className="collector-status-badge" data-status={label}>
      {label}
    </span>
  );
}
