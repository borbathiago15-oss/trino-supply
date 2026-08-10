"use client";

import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, PayingCompanyView, SupplierFullView, SupplierStatsView } from "@/lib/api";
import { Button, Card, Empty, Input, StatusPill, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";

const emptyPaying = {
  code: "", legalName: "", taxId: "", stateRegistration: "", address: "", district: "",
  city: "", state: "", zipCode: "", phone: "", email: "",
};
const emptySupplier = {
  code: "", name: "", taxId: "", stateRegistration: "", address: "", district: "",
  city: "", state: "", zipCode: "", phone: "", email: "", paymentTerms: "", paymentMethod: "",
};

export default function CadastrosPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const has = useHas();
  const canManage = has(Perm.PurchasesOrder);

  const onErr = (e: unknown) => toast.push("error", e instanceof ApiError ? e.message : "Erro");

  // ---- Empresas pagadoras (CNPJs do grupo) ----
  const paying = useQuery({ queryKey: ["paying-companies"], queryFn: () => api<PayingCompanyView[]>("/purchases/paying-companies") });
  const [pc, setPc] = useState(emptyPaying);
  const createPaying = useMutation({
    mutationFn: () => api("/purchases/paying-companies", { method: "POST", body: JSON.stringify(pc) }),
    onSuccess: () => { setPc(emptyPaying); qc.invalidateQueries({ queryKey: ["paying-companies"] }); toast.push("success", "Empresa pagadora cadastrada."); },
    onError: onErr,
  });

  // ---- Fornecedores (com dados fiscais) ----
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: () => api<SupplierFullView[]>("/purchases/suppliers") });
  const stats = useQuery({ queryKey: ["supplier-stats"], queryFn: () => api<SupplierStatsView[]>("/purchases/suppliers/stats") });
  const money = (v: number) => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const [sup, setSup] = useState(emptySupplier);
  const createSupplier = useMutation({
    mutationFn: () => api("/purchases/suppliers", { method: "POST", body: JSON.stringify(sup) }),
    onSuccess: () => { setSup(emptySupplier); qc.invalidateQueries({ queryKey: ["suppliers"] }); toast.push("success", "Fornecedor cadastrado."); },
    onError: onErr,
  });

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Cadastros</h1>
      <p className="text-sm text-slate-500">
        Empresas pagadoras (os CNPJs do grupo que pagam a compra) e fornecedores. Na emissão da OC você
        escolhe qual empresa paga e qual fornecedor venceu a concorrência.
      </p>

      <Card title="Empresas pagadoras (CNPJs)">
        {canManage && (
          <form className="mb-5 grid grid-cols-1 gap-2 sm:grid-cols-3"
            onSubmit={(e: FormEvent) => { e.preventDefault(); createPaying.mutate(); }}>
            <Input label="Código" value={pc.code} onChange={(e) => setPc({ ...pc, code: e.target.value })} />
            <Input label="Razão social" className="sm:col-span-2" value={pc.legalName} onChange={(e) => setPc({ ...pc, legalName: e.target.value })} />
            <Input label="CNPJ" value={pc.taxId} onChange={(e) => setPc({ ...pc, taxId: e.target.value })} />
            <Input label="Inscr. Estadual" value={pc.stateRegistration} onChange={(e) => setPc({ ...pc, stateRegistration: e.target.value })} />
            <Input label="Endereço" value={pc.address} onChange={(e) => setPc({ ...pc, address: e.target.value })} />
            <Input label="Bairro" value={pc.district} onChange={(e) => setPc({ ...pc, district: e.target.value })} />
            <Input label="Cidade" value={pc.city} onChange={(e) => setPc({ ...pc, city: e.target.value })} />
            <Input label="UF" maxLength={2} value={pc.state} onChange={(e) => setPc({ ...pc, state: e.target.value })} />
            <Input label="CEP" value={pc.zipCode} onChange={(e) => setPc({ ...pc, zipCode: e.target.value })} />
            <Input label="Telefone" value={pc.phone} onChange={(e) => setPc({ ...pc, phone: e.target.value })} />
            <Input label="E-mail" className="sm:col-span-2" value={pc.email} onChange={(e) => setPc({ ...pc, email: e.target.value })} />
            <div className="sm:col-span-3"><Button type="submit" disabled={createPaying.isPending}>Cadastrar empresa pagadora</Button></div>
          </form>
        )}
        {paying.data && paying.data.length > 0 ? (
          <Table head={["Código", "Razão social", "CNPJ", "Cidade/UF", "Situação"]}>
            {paying.data.map((p) => (
              <tr key={p.id}>
                <td className="px-3 py-2 font-mono text-xs">{p.code}</td>
                <td className="px-3 py-2">{p.legalName}</td>
                <td className="px-3 py-2 text-slate-500">{p.taxId}</td>
                <td className="px-3 py-2 text-slate-500">{[p.city, p.state].filter(Boolean).join("/")}</td>
                <td className="px-3 py-2"><StatusPill status={p.status} /></td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhuma empresa pagadora cadastrada.</Empty>
        )}
      </Card>

      <Card title="Fornecedores">
        {canManage && (
          <form className="mb-5 grid grid-cols-1 gap-2 sm:grid-cols-3"
            onSubmit={(e: FormEvent) => { e.preventDefault(); createSupplier.mutate(); }}>
            <Input label="Código" value={sup.code} onChange={(e) => setSup({ ...sup, code: e.target.value })} />
            <Input label="Nome" className="sm:col-span-2" value={sup.name} onChange={(e) => setSup({ ...sup, name: e.target.value })} />
            <Input label="CNPJ" value={sup.taxId} onChange={(e) => setSup({ ...sup, taxId: e.target.value })} />
            <Input label="Inscr. Estadual" value={sup.stateRegistration} onChange={(e) => setSup({ ...sup, stateRegistration: e.target.value })} />
            <Input label="Endereço" value={sup.address} onChange={(e) => setSup({ ...sup, address: e.target.value })} />
            <Input label="Bairro" value={sup.district} onChange={(e) => setSup({ ...sup, district: e.target.value })} />
            <Input label="Cidade" value={sup.city} onChange={(e) => setSup({ ...sup, city: e.target.value })} />
            <Input label="UF" maxLength={2} value={sup.state} onChange={(e) => setSup({ ...sup, state: e.target.value })} />
            <Input label="CEP" value={sup.zipCode} onChange={(e) => setSup({ ...sup, zipCode: e.target.value })} />
            <Input label="Telefone" value={sup.phone} onChange={(e) => setSup({ ...sup, phone: e.target.value })} />
            <Input label="Cond. Pgto (ex.: À Vista)" value={sup.paymentTerms} onChange={(e) => setSup({ ...sup, paymentTerms: e.target.value })} />
            <Input label="Forma Pgto (ex.: Depósito)" value={sup.paymentMethod} onChange={(e) => setSup({ ...sup, paymentMethod: e.target.value })} />
            <div className="sm:col-span-3"><Button type="submit" disabled={createSupplier.isPending}>Cadastrar fornecedor</Button></div>
          </form>
        )}
        {suppliers.data && suppliers.data.length > 0 ? (
          <Table head={["Código", "Nome", "CNPJ", "Cond. Pgto", "Situação"]}>
            {suppliers.data.map((s) => (
              <tr key={s.id}>
                <td className="px-3 py-2 font-mono text-xs">{s.code}</td>
                <td className="px-3 py-2">{s.name}</td>
                <td className="px-3 py-2 text-slate-500">{s.taxId}</td>
                <td className="px-3 py-2 text-slate-500">{s.paymentTerms}</td>
                <td className="px-3 py-2"><StatusPill status={s.status} /></td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhum fornecedor cadastrado.</Empty>
        )}
      </Card>

      <Card title="Histórico por fornecedor">
        <p className="mb-3 text-xs text-slate-500">
          Projeção atualizada de forma assíncrona a partir das OCs emitidas (pode levar alguns segundos após emitir).
        </p>
        {stats.data && stats.data.length > 0 ? (
          <Table head={["Fornecedor", "OCs", "Valor total", "Última OC"]}>
            {stats.data.map((s) => (
              <tr key={s.supplierId}>
                <td className="px-3 py-2">{s.code} — {s.name}</td>
                <td className="px-3 py-2 tabular-nums">{s.ordersCount}</td>
                <td className="px-3 py-2 text-right tabular-nums">R$ {money(s.totalValue)}</td>
                <td className="px-3 py-2 text-slate-500">{s.lastOrderAt ? new Date(s.lastOrderAt).toLocaleDateString("pt-BR") : "—"}</td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Sem histórico ainda — emita uma OC para começar a compor as estatísticas.</Empty>
        )}
      </Card>
    </>
  );
}
