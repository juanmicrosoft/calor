'use client';

import { useEffect, useRef } from 'react';

interface DrawerProps {
  id: string;
  label: string;
  open: boolean;
  onClose: () => void;
  children: React.ReactNode;
}

export function Drawer({ id, label, open, onClose, children }: DrawerProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const close = useRef(onClose);
  close.current = onClose;

  useEffect(() => {
    if (!open) return;
    const dialog = ref.current!;
    const previousOverflow = document.body.style.overflow;
    const trigger = document.activeElement as HTMLElement | null;
    dialog.showModal();
    document.body.style.overflow = 'hidden';
    const desktop = window.matchMedia('(min-width: 1024px)');
    const resized = () => { if (desktop.matches) close.current(); };
    desktop.addEventListener('change', resized);
    return () => {
      desktop.removeEventListener('change', resized);
      dialog.close();
      document.body.style.overflow = previousOverflow;
      if (trigger?.isConnected) trigger.focus();
    };
  }, [open]);

  return (
    <dialog
      ref={ref}
      id={id}
      aria-label={label}
      aria-modal="true"
      className="fixed inset-0 m-0 h-dvh w-screen max-h-none max-w-none border-0 bg-background/80 p-0 text-foreground backdrop-blur-sm"
      onCancel={event => { event.preventDefault(); onClose(); }}
      onClick={event => { if (event.target === event.currentTarget) onClose(); }}
      onKeyDown={event => {
        if (event.key !== 'Tab') return;
        const elements = Array.from(ref.current!.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex="0"]'
        )).filter(element => element.getClientRects().length > 0);
        const first = elements[0];
        const last = elements[elements.length - 1];
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first?.focus();
        }
      }}
    >
      {children}
    </dialog>
  );
}
