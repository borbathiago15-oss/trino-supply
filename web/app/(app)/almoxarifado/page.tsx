"use client";

import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, CollaboratorView, ConsumptionView, download } from "@/lib/api";
import { Button, Card, Empty, Input, Select, StatusPill, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";

const MOTIVOS = ["Nova contratação", "Substituição", "Troca de tamanho", "Danificado", "Perda", "Roubo"];

export default function AlmoxarifadoPage() {
  const has = useHas();
  const canManage = has(Perm.MaterialsManage);

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Almoxarifado</h1>
      <p className="text-sm text-slate-500">
        Entrega de EPI e fardamento aos colaboradores. Cada baixa dá saída no estoque e gera a
        <strong> Ficha de Entrega</strong> pré-preenchida para coleta de assinatura.
      </p>

      {canManage && <Colaboradores />}
      {canManage && <BaixaConsumo />}
      <Baixas />
    </>
  );
}

/** Cadastro leve de colaboradores (quem recebe o EPI/fardamento). */
function Colaboradores() {
  const qc = useQueryClient();
  const toast = useToast();
  const empty = { name: "", registration: "", costCenterCode: "", companyCode: "", admissionDate: "" };
  const [c, setC] = useState(empty);

  const list = useQuery({ queryKey: ["collaborators"], queryFn: () => api<CollaboratorView[]>("/materials/collaborators") });
  const create = useMutation({
    mutationFn: () => api("/materials/collaborators", {
      method: "POST",
      body: JSON.stringify({
        name: c.name, registration: c.registration || null, costCenterCode: c.costCenterCode || null,
        companyCode: c.companyCode || null, admissionDate: c.admissionDate || null,
      }),
    }),
    onSuccess: () => { setC(empty); qc.invalidateQueries({ queryKey: ["collaborators"] }); toast.push("success", "Colaborador cadastrado."); },
    onError: (e) => toast.push("error", e instanceof ApiError ? e.message : "Erro"),
  });

  return (
    <Card title="Colaboradores">
      <form className="mb-5 grid grid-cols-1 gap-2 sm:grid-cols-3" onSubmit={(e: FormEvent) => { e.preventDefault(); create.mutate(); }}>
        <Input label="Nome" value={c.name} onChange={(e) => setC({ ...c, name: e.target.value })} />
        <Input label="Matrícula" value={c.registration} onChange={(e) => setC({ ...c, registration: e.target.value })} />
        <Input label="Data de contratação" type="date" value={c.admissionDate} onChange={(e) => setC({ ...c, admissionDate: e.target.value })} />
        <Input label="Centro de custo (código)" value={c.costCenterCode} onChange={(e) => setC({ ...c, costCenterCode: e.target.value })} />
        <Input label="Empresa (código)" value={c.companyCode} onChange={(e) => setC({ ...c, companyCode: e.target.value })} />
        <div className="flex items-end"><Button type="submit" disabled={create.isPending}>Cadastrar colaborador</Button></div>
      </form>
      {list.data && list.data.length > 0 ? (
        <Table head={["Nome", "Matrícula", "Centro", "Empresa", "Contratação", "Situação"]}>
          {list.data.map((x) => (
            <tr key={x.id}>
              <td className="px-3 py-2">{x.name}</td>
              <td className="px-3 py-2 text-slate-500">{x.registration || "—"}</td>
              <td className="px-3 py-2 text-slate-500">{x.costCenterCode || "—"}</td>
              <td className="px-3 py-2 text-slate-500">{x.companyCode || "—"}</td>
              <td className="px-3 py-2 text-slate-500">{x.admissionDate ? new Date(x.admissionDate).toLocaleDateString("pt-BR") : "—"}</td>
              <td className="px-3 py-2"><StatusPill status={x.status} /></td>
            </tr>
          ))}
        </Table>
      ) : (
        <Empty>Nenhum colaborador cadastrado.</Empty>
      )}
    </Card>
  );
}

/** Baixa de consumo: empresa + centro + colaborador + motivo + produtos entregues. */
function BaixaConsumo() {
  const qc = useQueryClient();
  const toast = useToast();
  const collaborators = useQuery({ queryKey: ["collaborators"], queryFn: () => api<CollaboratorView[]>("/materials/collaborators") });

  const [hdr, setHdr] = useState({ companyCode: "", costCenterCode: "", collaboratorId: "", reason: "Nova contratação" });
  const [linhas, setLinhas] = useState<{ itemCode: string; quantity: string }[]>([]);
  const [item, setItem] = useState({ itemCode: "", quantity: "" });

  const onSelectColab = (id: string) => {
    const col = collaborators.data?.find((c) => c.id === id);
    setHdr({
      ...hdr, collaboratorId: id,
      companyCode: hdr.companyCode || col?.companyCode || "",
      costCenterCode: hdr.costCenterCode || col?.costCenterCode || "",
    });
  };

  const addItem = () => {
    const qty = Number(item.quantity.replace(",", "."));
    if (!item.itemCode.trim()) { toast.push("error", "Informe o código do produto."); return; }
    if (!(qty > 0)) { toast.push("error", "Quantidade deve ser positiva."); return; }
    setLinhas([...linhas, { itemCode: item.itemCode.trim().toUpperCase(), quantity: String(qty) }]);
    setItem({ itemCode: "", quantity: "" });
  };

  const baixar = useMutation({
    mutationFn: () => api<{ consumptionId: string }>("/materials/consumptions", {
      method: "POST",
      body: JSON.stringify({
        companyCode: hdr.companyCode, costCenterCode: hdr.costCenterCode, collaboratorId: hdr.collaboratorId,
        reason: hdr.reason, lines: linhas.map((l) => ({ itemCode: l.itemCode, quantity: Number(l.quantity) })),
      }),
    }),
    onSuccess: async (r) => {
      setLinhas([]); setHdr({ companyCode: "", costCenterCode: "", collaboratorId: "", reason: "Nova contratação" });
      qc.invalidateQueries({ queryKey: ["consumptions"] });
      qc.invalidateQueries({ queryKey: ["balance"] });
      toast.push("success", "Baixa registrada. Gerando a ficha…");
      try { await download(`/materials/consumptions/${r.consumptionId}/ficha`, "ficha-entrega-epi.pdf"); }
      catch { /* a ficha também pode ser baixada na lista abaixo */ }
    },
    onError: (e) => toast.push("error", e instanceof ApiError ? e.message : "Erro"),
  });

  const submeter = () => {
    if (!hdr.companyCode.trim()) { toast.push("error", "Informe a empresa."); return; }
    if (!hdr.costCenterCode.trim()) { toast.push("error", "Informe o centro de custo."); return; }
    if (!hdr.collaboratorId) { toast.push("error", "Selecione o colaborador."); return; }
    if (linhas.length === 0) { toast.push("error", "Adicione ao menos um produto."); return; }
    baixar.mutate();
  };

  return (
    <Card title="Baixa de consumo (entrega de EPI/Fardamento)">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Select label="Colaborador" value={hdr.collaboratorId} onChange={(e) => onSelectColab(e.target.value)}>
          <option value="">Selecione…</option>
          {(collaborators.data ?? []).map((c) => (
            <option key={c.id} value={c.id}>{c.name}{c.registration ? ` (${c.registration})` : ""}</option>
          ))}
        </Select>
        <Select label="Motivo" value={hdr.reason} onChange={(e) => setHdr({ ...hdr, reason: e.target.value })}>
          {MOTIVOS.map((m) => <option key={m} value={m}>{m}</option>)}
        </Select>
        <Input label="Empresa (código)" value={hdr.companyCode} onChange={(e) => setHdr({ ...hdr, companyCode: e.target.value })} />
        <Input label="Centro de custo (código)" value={hdr.costCenterCode} onChange={(e) => setHdr({ ...hdr, costCenterCode: e.target.value })} />
      </div>

      <form className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-3" onSubmit={(e: FormEvent) => { e.preventDefault(); addItem(); }}>
        <Input label="Produto (código)" value={item.itemCode} onChange={(e) => setItem({ ...item, itemCode: e.target.value })} />
        <Input label="Quantidade" inputMode="decimal" value={item.quantity} onChange={(e) => setItem({ ...item, quantity: e.target.value })} />
        <div className="flex items-end"><Button type="submit" variant="ghost">Adicionar produto</Button></div>
      </form>

      {linhas.length > 0 && (
        <div className="mt-4">
          <Table head={["Produto", "Qtd", ""]}>
            {linhas.map((l, i) => (
              <tr key={i}>
                <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
                <td className="px-3 py-2">{Number(l.quantity)}</td>
                <td className="px-3 py-2 text-right">
                  <button className="text-xs text-rose-600 hover:underline" onClick={() => setLinhas(linhas.filter((_, j) => j !== i))}>remover</button>
                </td>
              </tr>
            ))}
          </Table>
          <div className="mt-3">
            <Button onClick={submeter} disabled={baixar.isPending}>
              {baixar.isPending ? "Registrando…" : `Registrar baixa e gerar ficha (${linhas.length})`}
            </Button>
          </div>
        </div>
      )}
    </Card>
  );
}

/** Baixas recentes com download da Ficha de Entrega (PDF). */
function Baixas() {
  const toast = useToast();
  const list = useQuery({ queryKey: ["consumptions"], queryFn: () => api<ConsumptionView[]>("/materials/consumptions") });

  const baixarFicha = async (id: string) => {
    try { await download(`/materials/consumptions/${id}/ficha`, "ficha-entrega-epi.pdf"); }
    catch (e) { toast.push("error", e instanceof ApiError ? e.message : "Erro"); }
  };

  return (
    <Card title="Baixas de consumo">
      {list.data && list.data.length > 0 ? (
        <Table head={["Data", "Colaborador", "Centro", "Motivo", "Itens", "Ficha"]}>
          {list.data.map((x) => (
            <tr key={x.id}>
              <td className="px-3 py-2 text-slate-500">{new Date(x.issuedAt).toLocaleDateString("pt-BR")}</td>
              <td className="px-3 py-2">{x.collaboratorName}</td>
              <td className="px-3 py-2 text-slate-500">{x.costCenterCode}</td>
              <td className="px-3 py-2 text-slate-500">{x.reason}</td>
              <td className="px-3 py-2 text-xs text-slate-500">{x.lines.map((l) => `${l.itemCode}×${Number(l.quantity)}`).join(", ")}</td>
              <td className="px-3 py-2"><Button variant="ghost" onClick={() => baixarFicha(x.id)}>Ficha (PDF)</Button></td>
            </tr>
          ))}
        </Table>
      ) : (
        <Empty>Nenhuma baixa registrada ainda.</Empty>
      )}
    </Card>
  );
}
