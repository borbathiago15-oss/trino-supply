'use strict';

// Bootstrap do primeiro tenant + usuário administrador (Fase F1).
// Uso: npm --workspace @trino/api run seed
// Variáveis: SEED_CNPJ, SEED_RAZAO_SOCIAL, SEED_ADMIN_NOME, SEED_ADMIN_EMAIL, SEED_ADMIN_SENHA
// A senha NUNCA é gravada em claro: só o hash argon2id vai para core.usuario.

require('dotenv').config();
const argon2 = require('argon2');
const { PrismaClient, forTenant } = require('@trino/db');

const prisma = new PrismaClient();

async function main() {
  const cnpj = (process.env.SEED_CNPJ ?? '11222333000181').replace(/\D/g, '');
  const razaoSocial = process.env.SEED_RAZAO_SOCIAL ?? 'Grupo Trino LTDA';
  const nome = process.env.SEED_ADMIN_NOME ?? 'Administrador Trino';
  const email = (process.env.SEED_ADMIN_EMAIL ?? 'admin@trino.com.br').trim().toLowerCase();
  const senha = process.env.SEED_ADMIN_SENHA;

  if (!senha || senha.length < 8) {
    throw new Error('Defina SEED_ADMIN_SENHA com ao menos 8 caracteres — não há senha padrão.');
  }

  const tenant = await prisma.tenant.upsert({
    where: { cnpj },
    update: { razaoSocial },
    create: { cnpj, razaoSocial },
  });

  const db = forTenant(prisma, tenant.id);
  const senhaHash = await argon2.hash(senha, { type: argon2.argon2id });
  const admin = await db.usuario.upsert({
    where: { tenantId_email: { tenantId: tenant.id, email } },
    update: { nome, senhaHash, ativo: true },
    create: { nome, email, senhaHash, cargoFuncional: 'Administrador' },
  });

  console.log(`tenant  ${tenant.cnpj}  ${tenant.id}`);
  console.log(`admin   ${admin.email}  ${admin.id}`);
}

main()
  .catch((e) => {
    console.error(e.message);
    process.exitCode = 1;
  })
  .finally(() => prisma.$disconnect());
