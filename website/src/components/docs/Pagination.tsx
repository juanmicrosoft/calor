import Link from 'next/link';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { DocMeta } from '@/lib/docs';

interface PaginationProps {
  prev: DocMeta | null;
  next: DocMeta | null;
}

export function Pagination({ prev, next }: PaginationProps) {
  return (
    <nav className="mt-12 flex items-center justify-between border-t pt-6">
      {prev ? (
        <Link
          href={`/docs/${prev.slug}/`}
          className="group flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"
        >
          <ChevronLeft className="h-4 w-4 transition-transform group-hover:-translate-x-1" />
          <div className="flex flex-col">
            <span className="text-xs">Previous</span>
            <span className="font-medium text-foreground">{prev.title}</span>
          </div>
        </Link>
      ) : (
        <div />
      )}

      {next ? (
        <Link
          href={`/docs/${next.slug}/`}
          className="group flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground text-right"
        >
          <div className="flex flex-col">
            <span className="text-xs">Next</span>
            <span className="font-medium text-foreground">{next.title}</span>
          </div>
          <ChevronRight className="h-4 w-4 transition-transform group-hover:translate-x-1" />
        </Link>
      ) : (
        <div />
      )}
    </nav>
  );
}
