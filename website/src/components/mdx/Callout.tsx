import { AlertCircle, Info, AlertTriangle, CheckCircle, Lightbulb } from 'lucide-react';
import { cn } from '@/lib/utils';

type CalloutType = 'note' | 'warning' | 'important' | 'tip' | 'highlight';

interface CalloutProps {
  type?: CalloutType;
  title?: string;
  children: React.ReactNode;
}

const calloutStyles: Record<
  CalloutType,
  { icon: React.ReactNode; className: string; defaultTitle: string }
> = {
  note: {
    icon: <Info className="h-5 w-5" />,
    className: 'border-blue-500/50 bg-blue-500/10 text-blue-700 dark:text-blue-300',
    defaultTitle: 'Note',
  },
  warning: {
    icon: <AlertTriangle className="h-5 w-5" />,
    className: 'border-yellow-500/50 bg-yellow-500/10 text-yellow-700 dark:text-yellow-300',
    defaultTitle: 'Warning',
  },
  important: {
    icon: <AlertCircle className="h-5 w-5" />,
    className: 'border-red-500/50 bg-red-500/10 text-red-700 dark:text-red-300',
    defaultTitle: 'Important',
  },
  tip: {
    icon: <Lightbulb className="h-5 w-5" />,
    className: 'border-green-500/50 bg-green-500/10 text-green-700 dark:text-green-300',
    defaultTitle: 'Tip',
  },
  highlight: {
    icon: <CheckCircle className="h-5 w-5" />,
    className: 'border-purple-500/50 bg-purple-500/10 text-purple-700 dark:text-purple-300',
    defaultTitle: 'Highlight',
  },
};

export function Callout({ type = 'note', title, children }: CalloutProps) {
  const style = calloutStyles[type];

  return (
    <div
      className={cn(
        'my-6 rounded-lg border-l-4 p-4',
        style.className
      )}
    >
      <div className="flex items-start gap-3">
        <div className="mt-0.5 shrink-0">{style.icon}</div>
        <div>
          <p className="font-semibold mb-1">{title || style.defaultTitle}</p>
          <div className="text-sm opacity-90 [&>p]:my-0">{children}</div>
        </div>
      </div>
    </div>
  );
}
