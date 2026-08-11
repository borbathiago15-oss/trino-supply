"use client";

import { useQuery } from "@tanstack/react-query";
import { api, CostCenterView, PayingCompanyView } from "@/lib/api";
import { Input, Select } from "@/components/ui";

/** Cabeçalho comum a toda solicitação de compra (spec Sistema de Compras). */
export interface ReqHeader {
  payingCompanyCode: string;
  costCenterCode: string;
  priority: string;
  justification: string;
  approverLevel1Subject: string;
  approverLevel2Subject: string;
}

export const emptyHeader: ReqHeader = {
  payingCompanyCode: "", costCenterCode: "", priority: "Normal",
  justification: "", approverLevel1Subject: "", approverLevel2Subject: "",
};

export interface Approver { subject: string; displayName: string; email: string }

/** Falta algum campo obrigatório do cabeçalho? (mensagem amigável para toast). */
export function headerError(h: ReqHeader): string | null {
  if (!h.payingCompanyCode) return "Selecione a empresa do custo.";
  if (!h.costCenterCode) return "Selecione o centro de custo.";
  if (!h.justification.trim()) return "Informe o motivo/justificativa.";
  if (!h.approverLevel1Subject) return "Selecione o aprovador de nível 1.";
  if (!h.approverLevel2Subject) return "Selecione o aprovador de nível 2.";
  if (h.approverLevel1Subject === h.approverLevel2Subject) return "Os aprovadores devem ser distintos.";
  return null;
}

/** Carrega os dados de referência do cabeçalho (empresas do custo, centros e aprovadores). */
export function useRequisitionRefData(enabled = true) {
  const paying = useQuery({ queryKey: ["paying-companies"], queryFn: () => api<PayingCompanyView[]>("/purchases/paying-companies"), enabled });
  const centers = useQuery({ queryKey: ["cost-centers"], queryFn: () => api<CostCenterView[]>("/purchases/cost-centers"), enabled });
  const approvers = useQuery({ queryKey: ["approvers"], queryFn: () => api<Approver[]>("/purchases/approvers"), enabled });
  return { paying, centers, approvers };
}

/** Campos do cabeçalho: empresa do custo (CNPJ), centro de custo, tipo de demanda, motivo e 2 aprovadores. */
export function RequisitionHeaderFields({
  value, onChange, paying, centers, approvers,
}: {
  value: ReqHeader;
  onChange: (h: ReqHeader) => void;
  paying?: PayingCompanyView[];
  centers?: CostCenterView[];
  approvers?: Approver[];
}) {
  const set = (patch: Partial<ReqHeader>) => onChange({ ...value, ...patch });
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
      <Select label="Empresa do custo (CNPJ)" value={value.payingCompanyCode} onChange={(e) => set({ payingCompanyCode: e.target.value })}>
        <option value="">Selecione…</option>
        {(paying ?? []).map((p) => <option key={p.id} value={p.code}>{p.code} — {p.legalName}</option>)}
      </Select>
      <Select label="Centro de custo" value={value.costCenterCode} onChange={(e) => set({ costCenterCode: e.target.value })}>
        <option value="">Selecione…</option>
        {(centers ?? []).map((c) => <option key={c.id} value={c.code}>{c.code} — {c.name}</option>)}
      </Select>
      <Select label="Tipo de demanda" value={value.priority} onChange={(e) => set({ priority: e.target.value })}>
        <option value="Normal">Normal</option>
        <option value="Emergencial">Emergencial</option>
      </Select>
      <Input label="Motivo / justificativa" value={value.justification} onChange={(e) => set({ justification: e.target.value })} />
      <Select label="Aprovador — nível 1" value={value.approverLevel1Subject} onChange={(e) => set({ approverLevel1Subject: e.target.value })}>
        <option value="">Selecione…</option>
        {(approvers ?? []).map((a) => <option key={a.subject} value={a.subject}>{a.displayName || a.subject}</option>)}
      </Select>
      <Select label="Aprovador — nível 2" value={value.approverLevel2Subject} onChange={(e) => set({ approverLevel2Subject: e.target.value })}>
        <option value="">Selecione…</option>
        {(approvers ?? []).map((a) => <option key={a.subject} value={a.subject}>{a.displayName || a.subject}</option>)}
      </Select>
    </div>
  );
}
