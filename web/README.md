# Trino Supply — Web (Next.js)

Frontend em **Next.js / React / TypeScript** (TPES-002, ARC-001), com **Tailwind**, **Zod** (validação), **TanStack Query** (estado de servidor) e **Zustand** (estado de UI) — stack de front confirmada no GO-001 / ADR-015.

## Estrutura prevista (por workspace / papel — MMS-001 §12, `*-14-ux`)
```
web/
├── app/                 # App Router (Next.js)
│   ├── (requester)/     # Requester Workspace
│   ├── (warehouse)/     # Warehouse Workspace
│   ├── (approvals)/     # Fila de aprovações
│   └── (management)/    # KPIs / gestão
├── lib/                 # cliente de API (/api/v1), auth, query client
└── components/          # design system (DS-001)
```

## Como iniciar (dev)
```bash
cd web
npm install
npm run dev
```

> Este é o esqueleto (GO-001, item 0). O scaffold completo do Next.js (App Router, Tailwind config, cliente de API tipado por Zod) entra na fase de frontend, após as APIs do Foundation/MMS.
