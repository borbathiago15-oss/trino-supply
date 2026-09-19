"use client";

import { useEffect, useRef, useState } from "react";

// BarcodeDetector ainda não está na lib DOM do TypeScript — só o que usamos aqui.
type Detected = { rawValue: string };
type Detector = { detect: (source: HTMLVideoElement) => Promise<Detected[]> };
type DetectorCtor = new (opts: { formats: string[] }) => Detector;

const FORMATOS = ["code_128", "ean_13", "ean_8", "code_39", "qr_code"];

function detectorCtor(): DetectorCtor | null {
  if (typeof window === "undefined") return null;
  return (window as unknown as { BarcodeDetector?: DetectorCtor }).BarcodeDetector ?? null;
}

/** O aparelho lê código de barras pela câmara? (Chrome/Android sim; Safari/iOS ainda não.) */
export function useBarcodeSupport(): boolean {
  const [ok, setOk] = useState(false);
  useEffect(() => { setOk(detectorCtor() !== null && !!navigator.mediaDevices?.getUserMedia); }, []);
  return ok;
}

/**
 * Leitor pela câmara traseira. Entrega cada código lido uma vez (o mesmo código só volta depois
 * de sair do quadro por um instante) — bipar a mesma caixa duas vezes é decisão de quem confere.
 */
export function Scanner({ onRead, onClose }: { onRead: (text: string) => void; onClose: () => void }) {
  const video = useRef<HTMLVideoElement>(null);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    const Ctor = detectorCtor();
    if (!Ctor || !video.current) return;
    const detector = new Ctor({ formats: FORMATOS });
    let stream: MediaStream | null = null;
    let timer: number | undefined;
    let ultimo = "";
    let vivo = true;

    (async () => {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: "environment" } } });
        if (!vivo || !video.current) return;
        video.current.srcObject = stream;
        await video.current.play();
      } catch {
        setErro("Sem acesso à câmara. Digite o código abaixo.");
        return;
      }
      const ler = async () => {
        if (!vivo || !video.current) return;
        try {
          const achados = await detector.detect(video.current);
          const texto = achados[0]?.rawValue?.trim();
          if (texto && texto !== ultimo) { ultimo = texto; onRead(texto); }
          if (!texto) ultimo = "";
        } catch { /* quadro ruim — tenta no próximo */ }
        timer = window.setTimeout(ler, 250);
      };
      ler();
    })();

    return () => {
      vivo = false;
      if (timer) window.clearTimeout(timer);
      stream?.getTracks().forEach((t) => t.stop());
    };
  }, [onRead]);

  return (
    <div className="space-y-2">
      <div className="relative overflow-hidden rounded-xl bg-black">
        <video ref={video} className="h-56 w-full object-cover" muted playsInline />
        <div className="pointer-events-none absolute inset-x-8 top-1/2 h-0.5 -translate-y-1/2 bg-rose-500/80" />
      </div>
      {erro && <p className="text-xs text-rose-600">{erro}</p>}
      <button type="button" onClick={onClose} className="w-full rounded-lg border border-slate-300 py-2 text-sm">
        Fechar câmara
      </button>
    </div>
  );
}
