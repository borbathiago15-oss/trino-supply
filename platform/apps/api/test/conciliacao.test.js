'use strict';

// Testes UNITÁRIOS da conciliação 3-way e do recebimento parcial/total
// (Fase F6) — domínio puro:  node test/conciliacao.test.js

const {
  conciliarRecebimento,
  ConciliacaoError,
  TOLERANCIA_EXCESSO,
} = require('../dist/pedidos/dominio/conciliacao');

let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};
const codigoDe = (fn) => {
  try { fn(); return 'OK'; }
  catch (e) { return e instanceof ConciliacaoError ? e.codigo : `INESPERADO:${e.message}`; }
};

const item = (id, qtdPedida, preco, acumulada = 0) => ({
  id, varianteId: `v-${id}`, qtdPedida, qtdRecebidaAcumulada: acumulada, precoUnitario: preco,
});

console.log('== recebimento TOTAL em uma remessa');
const total = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100), item('i2', 5, 50)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 10 }, { itemPedidoId: 'i2', qtdRecebida: 5 }],
});
check('tudo entregue vira TOTAL', total.tipo === 'TOTAL');
check('sem divergência, está conciliado', total.conciliado === true && total.divergencias.length === 0);
check('valor recebido = Σ qtd × preço do pedido', total.valorRecebido === 10 * 100 + 5 * 50);
check('tudo entra em estoque quando nada veio avariado',
  total.linhas.every((l) => l.qtdParaEstoque === l.qtdRecebida));
check('nenhum evento da Conta 408', total.eventosConta408.length === 0);

console.log('== recebimento PARCIAL e o acumulado entre remessas');
const parcial = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100), item('i2', 5, 50)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 4 }],
});
check('entrega incompleta vira PARCIAL', parcial.tipo === 'PARCIAL');
check('o item não entregue é listado como pendente',
  parcial.divergencias.some((d) => d.codigo === 'REC-DIV-003' && d.itemPedidoId === 'i2'));
check('o item entregue pela metade também fica pendente',
  parcial.divergencias.filter((d) => d.codigo === 'REC-DIV-003').length === 2);
check('a pendência informa quanto falta',
  parcial.divergencias.find((d) => d.itemPedidoId === 'i1').detalhe.falta === 6);
check('pendência de entrega NÃO desconcilia a remessa', parcial.conciliado === true);
check('a linha sabe que não completou', parcial.linhas[0].completo === false);

const segundaRemessa = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100, 4), item('i2', 5, 50, 5)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 6 }],
});
check('a remessa que fecha o acumulado vira TOTAL', segundaRemessa.tipo === 'TOTAL');
check('o acumulado soma as remessas anteriores', segundaRemessa.linhas[0].qtdAcumuladaApos === 10);
check('nenhuma pendência sobra', segundaRemessa.divergencias.length === 0);

const aindaFalta = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100, 4), item('i2', 5, 50, 0)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 6 }],
});
check('item que nunca veio segura o TOTAL, mesmo com o outro completo',
  aindaFalta.tipo === 'PARCIAL' && aindaFalta.divergencias.some((d) => d.itemPedidoId === 'i2'));

console.log('== avaria: não entra em estoque e vai para a Conta 408');
const comAvaria = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 10, qtdAvariada: 3, descricaoOcorrencia: 'Caixas molhadas' }],
});
check('só o íntegro entra em estoque', comAvaria.linhas[0].qtdParaEstoque === 7);
check('a ocorrência vira AVARIA sozinha', comAvaria.linhas[0].ocorrencia === 'AVARIA');
check('avaria gera evento na Conta 408',
  comAvaria.eventosConta408.length === 1 && comAvaria.eventosConta408[0].conta === '408' &&
  comAvaria.eventosConta408[0].tipo === 'AVARIA');
check('o evento traz o valor da perda (3 × 100)', comAvaria.eventosConta408[0].valor === 300);
check('o evento carrega a descrição informada', comAvaria.eventosConta408[0].descricao === 'Caixas molhadas');
check('avaria desconcilia o recebimento', comAvaria.conciliado === false);
check('quantidade total (com avaria) conta para o acumulado do pedido',
  comAvaria.linhas[0].qtdAcumuladaApos === 10 && comAvaria.tipo === 'TOTAL');

console.log('== excesso dentro e fora da tolerância');
const excesso = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 11 }],
});
check(`excesso de até ${TOLERANCIA_EXCESSO * 100}% é aceito`, excesso.tipo === 'TOTAL');
check('mas fica registrado como divergência',
  excesso.divergencias.some((d) => d.codigo === 'REC-DIV-002' && d.detalhe.excesso === 1));
check('e vira evento de divergência de quantidade na 408',
  excesso.eventosConta408.some((e) => e.tipo === 'DIVERGENCIA_QUANTIDADE' && e.valor === 100));
check('excesso acima da tolerância é RECUSADO (REC-ERR-006)',
  codigoDe(() => conciliarRecebimento({
    itensPedido: [item('i1', 10, 100)],
    linhas: [{ itemPedidoId: 'i1', qtdRecebida: 12 }],
  })) === 'REC-ERR-006');
check('a tolerância considera o acumulado, não a remessa isolada',
  codigoDe(() => conciliarRecebimento({
    itensPedido: [item('i1', 10, 100, 10)],
    linhas: [{ itemPedidoId: 'i1', qtdRecebida: 2 }],
  })) === 'REC-ERR-006');

console.log('== terceira via: a nota fiscal');
const nfCerta = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 10 }],
  notaFiscal: { id: 'nf1', valorTotal: 1000, fornecedorId: 'f1' },
});
check('NF batendo com o recebido fecha o 3-way',
  nfCerta.conciliado === true && nfCerta.valorNotaFiscal === 1000);

const nfDivergente = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 10 }],
  notaFiscal: { id: 'nf2', valorTotal: 1150, fornecedorId: 'f1' },
});
check('NF divergente do recebido é achado (REC-DIV-005)',
  nfDivergente.divergencias.some((d) => d.codigo === 'REC-DIV-005' && d.detalhe.diferenca === 150));
check('divergência de valor vai para a Conta 408',
  nfDivergente.eventosConta408.some((e) => e.tipo === 'DIVERGENCIA_VALOR' && e.valor === 150));
check('e o recebimento fica NÃO conciliado', nfDivergente.conciliado === false);

const centavos = conciliarRecebimento({
  itensPedido: [item('i1', 3, 33.33)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 3 }],
  notaFiscal: { id: 'nf3', valorTotal: 99.99, fornecedorId: 'f1' },
});
check('diferença de centavos não vira divergência', centavos.conciliado === true);

const parcialComNf = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 4 }],
  notaFiscal: { id: 'nf4', valorTotal: 400, fornecedorId: 'f1' },
});
check('NF de remessa parcial confere contra o que veio, não contra o pedido inteiro',
  parcialComNf.tipo === 'PARCIAL' && parcialComNf.conciliado === true && parcialComNf.valorRecebido === 400);

console.log('== especificação divergente');
const especificacao = conciliarRecebimento({
  itensPedido: [item('i1', 10, 100)],
  linhas: [{
    itemPedidoId: 'i1', qtdRecebida: 10,
    ocorrencia: 'DIVERGENCIA_ESPECIFICACAO', descricaoOcorrencia: 'Veio luva PU, pedido era nitrílica',
  }],
});
check('especificação divergente é registrada (REC-DIV-004)',
  especificacao.divergencias.some((d) => d.codigo === 'REC-DIV-004'));
check('e também vai para a Conta 408',
  especificacao.eventosConta408.some((e) => e.tipo === 'DIVERGENCIA_ESPECIFICACAO'));

console.log('== o que a conciliação recusa registrar');
check('remessa vazia (REC-ERR-001)',
  codigoDe(() => conciliarRecebimento({ itensPedido: [item('i1', 10, 100)], linhas: [] })) === 'REC-ERR-001');
check('mesmo item duas vezes na remessa (REC-ERR-002)',
  codigoDe(() => conciliarRecebimento({
    itensPedido: [item('i1', 10, 100)],
    linhas: [{ itemPedidoId: 'i1', qtdRecebida: 1 }, { itemPedidoId: 'i1', qtdRecebida: 2 }],
  })) === 'REC-ERR-002');
check('item que não é do pedido (REC-ERR-003)',
  codigoDe(() => conciliarRecebimento({
    itensPedido: [item('i1', 10, 100)],
    linhas: [{ itemPedidoId: 'outro', qtdRecebida: 1 }],
  })) === 'REC-ERR-003');
check('quantidade zero (REC-ERR-004)',
  codigoDe(() => conciliarRecebimento({
    itensPedido: [item('i1', 10, 100)],
    linhas: [{ itemPedidoId: 'i1', qtdRecebida: 0 }],
  })) === 'REC-ERR-004');
check('avaria maior que o recebido (REC-ERR-005)',
  codigoDe(() => conciliarRecebimento({
    itensPedido: [item('i1', 10, 100)],
    linhas: [{ itemPedidoId: 'i1', qtdRecebida: 5, qtdAvariada: 6 }],
  })) === 'REC-ERR-005');

console.log('== avaria total: nada entra em estoque');
const tudoAvariado = conciliarRecebimento({
  itensPedido: [item('i1', 4, 250)],
  linhas: [{ itemPedidoId: 'i1', qtdRecebida: 4, qtdAvariada: 4, descricaoOcorrencia: 'Carga tombou' }],
});
check('estoque recebe zero', tudoAvariado.linhas[0].qtdParaEstoque === 0);
check('a Conta 408 recebe o valor cheio', tudoAvariado.eventosConta408[0].valor === 1000);

console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
process.exitCode = fail ? 1 : 0;
