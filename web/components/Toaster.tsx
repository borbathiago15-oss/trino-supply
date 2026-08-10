"use client";

import { useToast } from "@/lib/toast";

export function Toaster() {
  const { toasts, dismiss } = useToast();
  return (
    <div className="pointer-events-none fixed bottom-4 right-4 z-50 flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2">
      {toasts.map((t) => (
        <button
          key={t.id}
          onClick={() => dismiss(t.id)}
          className={`pointer-events-auto flex items-start gap-2 rounded-lg px-4 py-3 text-left text-sm shadow-lg transition ${
            t.kind === "success"
              ? "bg-emerald-600 text-white"
              : "bg-rose-600 text-white"
          }`}
        >
          <span aria-hidden className="mt-0.5 font-semibold">
            {t.kind === "success" ? "✓" : "!"}
          </span>
          <span>{t.message}</span>
        </button>
      ))}
    </div>
  );
}
