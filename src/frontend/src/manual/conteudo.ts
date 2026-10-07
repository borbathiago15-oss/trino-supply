/**
 * O manual de cada tela, escrito a partir do que a tela faz e das regras que o servidor cobra.
 *
 * Fica no código, e não num cadastro, de propósito: a tela e o texto que a explica mudam no
 * mesmo commit e passam pela mesma revisão. Manual que descreve botão que não existe mais é
 * pior que manual nenhum — ensina errado com cara de oficial.
 *
 * As regras citam o código do erro quando existe: é o que aparece na mensagem do sistema, e
 * quem lê o manual depois de um "RFQ-ERR-030" acha a explicação pelo mesmo nome.
 */

export interface RegraDoManual {
  /** O código que o sistema mostra quando a regra barra alguma coisa. */
  codigo?: string;
  texto: string;
}

export interface Duvida {
  pergunta: string;
  resposta: string;
}

export interface Manual {
  /** Para que a tela serve, em uma ou duas frases. */
  paraQueServe: string;
  /** O caminho de uso comum, na ordem em que se faz. */
  passos: string[];
  /** O que o sistema cobra — e que a pessoa descobriria no erro se ninguém dissesse antes. */
  regras?: RegraDoManual[];
  duvidas?: Duvida[];
}

/** Chave = `id` do item do menu, ou a chave de uma tela de detalhe (ver `telas.ts`). */
export const MANUAIS: Record<string, Manual> = {
  // ------------------------------------------------------------------ Dashboard
  'director-view': {
    paraQueServe: 'A compra da empresa na ordem em que um diretor pergunta: quanto se gastou, quanto se economizou, o que espera a sua decisão e o que saiu do padrão.',
    passos: [
      'Escolha o período no topo (três opções, sem filtros avulsos).',
      'Leia os cinco números com tendência: gasto, saving, o que espera a sua aprovação, exceções e OTIF.',
      'O relatório em três frases resume o período com os mesmos números dos blocos.',
      '"O que espera a sua decisão" lista a fila — a decisão em si é tomada na Central de Aprovação.',
      '"O que chama atenção" traz os achados do Insights.',
      '"Solicitações de material ao almoxarifado" mostra, pelo mesmo período e centro do topo, as pendências de hoje, as atendidas, o tempo do pedido à entrega e a parte atendida no prazo, com os centros e as famílias que mais pedem. O detalhe fica no Painel de Atendimentos.',
    ],
    regras: [
      { texto: 'As três frases saem dos mesmos números da tabela: a frase nunca diz uma coisa e o bloco outra.' },
      { texto: 'OTIF sem entrega medida aparece como traço, não como 0% — zero diria que todo mundo atrasou.' },
    ],
  },
  'supply-dash': {
    paraQueServe: 'O painel do comprador: volume de solicitações e pedidos, prazos, saving e a Central de Avisos com o que está aberto para você agir.',
    passos: [
      'Período, empresa e centro de custo ficam à vista. Os demais (fornecedor, comprador, prioridade, categoria, família…) estão em "Mais filtros" — o número no botão diz quantos deles estão valendo. Clique em "Aplicar filtros".',
      'Cada filtro valendo aparece como etiqueta abaixo dos campos; tocar no ✕ o tira e refaz a conta.',
      'Os cartões do topo mostram solicitações, aprovações, valor comprado, pedidos em aberto, atrasos e tempos médios. O ⓘ de cada cartão diz de onde o número sai, e a linha colorida compara com a meta cadastrada, quando há.',
      '"Pontos de atenção" lista o que pede ação no período: SC atrasada, meta longe, OTIF abaixo de 70%, pedido parado, prazo estourado. Toque para ir à tela onde se resolve.',
      'No gráfico, passe o mouse ou toque na barra para ver o valor; toque no mês (ou de novo na barra) para filtrar o painel por aquele mês.',
      'Toque numa barra dos rankings (fornecedor, família, comprador…) para filtrar o painel inteiro por ela; toque de novo para tirar.',
      'No celular, as tabelas viram um card por linha, com as mesmas colunas.',
      'A Central de Avisos lista o que depende de você agora; o número muda sozinho quando o trabalho anda.',
      '"Limpar" volta ao recorte padrão.',
      '"Solicitações de material ao almoxarifado" é o bloco do material que sai do estoque sem compra, com filtros próprios (período, centro de custo, família e produto): pendentes, atendidas, tempos e os rankings por centro, família, produto e solicitante. Tocar a barra do centro ou da família filtra o bloco.',
    ],
    regras: [
      { texto: 'As pendências de material (aguardando aprovação e na fila do estoque) são de hoje, não do período: a solicitação de setembro que ninguém atendeu continua aparecendo. Solicitadas e atendidas são do período.' },
      { texto: 'O prazo de atendimento é o de Cadastros → Prazo de atendimento, contado da aprovação do centro — a mesma régua da Fila de Atendimento.' },
      { texto: 'O valor comprado conta pela data da aprovação da compra, que é quando o pedido nasce — e não pela data da O.C. do ERP, que deixaria de fora o pedido fechado com justificativa.' },
      { texto: 'O comprador de um pedido é quem conduziu a compra (escolheu o vencedor), e não quem aprovou.' },
      { texto: 'Sem base no período anterior, a variação não aparece: "sem base" é diferente de "cresceu 100%".' },
    ],
    duvidas: [
      { pergunta: 'Qual a diferença entre a Central de Avisos e o sino?', resposta: 'A Central conta o que está aberto agora. O sino guarda fatos datados — "esta SC passou a ser sua" — que continuam lá até você marcar como lido.' },
    ],
  },
  insights: {
    paraQueServe: 'Achados automáticos sobre a compra — sobrepreço, fracionamento, compras emergenciais, concentração em fornecedor, compra sem O.C., atraso de fornecedor e proposta única — e a visão executiva de spend, saving, OTIF e compliance.',
    passos: [
      'Leia os indicadores da visão executiva no topo.',
      'Na lista de achados, cada linha diz o que foi encontrado e com que severidade.',
      'Use "Tratar a causa" para abrir um ciclo de melhoria (PDCA) já com o problema e a evidência escritos.',
    ],
    regras: [
      { texto: 'O mesmo achado não abre dois ciclos: clicar de novo leva ao ciclo que já existe.' },
      { texto: 'Achado de severidade alta que aparece por três dias diferentes abre um plano de ação sozinho, sem responsável — o gestor recebe o aviso e distribui.' },
    ],
  },
  compliance: {
    paraQueServe: 'O Compliance Score de cada processo de compra: quanto do processo seguiu o padrão (cotações, justificativas, aprovações, O.C. do ERP).',
    passos: [
      'Veja o score médio e os processos críticos nos cartões do topo.',
      'As médias por comprador e por centro de custo mostram onde o padrão escapa com mais frequência.',
      'O risco de concentração aponta produtos comprados quase sempre do mesmo fornecedor.',
    ],
    regras: [
      { texto: 'O score mede e expõe, nunca bloqueia: um processo com score baixo continua podendo ser aprovado.' },
    ],
  },
  reports: {
    paraQueServe: 'Os relatórios de compras em cinco abas — Visão geral, Saving, Fornecedores, Demanda e prazos, Exceções — com catorze blocos numerados.',
    passos: [
      'Filtre por período, empresa, centro de custo ou comprador e clique em "Aplicar filtros".',
      'Escolha a aba. Cada bloco tem "como é calculado" recolhido embaixo do título.',
      '"Em três frases" resume o período com os mesmos números dos blocos.',
    ],
    regras: [
      { texto: 'O saving tem três réguas: negociação (contra a primeira proposta do vencedor), concorrência (contra a maior proposta completa — nula com um proponente só) e orçamento (só quando todas as SCs do processo informaram o seu).' },
      { texto: '"Sem O.C. do ERP" conta as compras fechadas com a observação da PO-BR-011 — o número de O.C. nunca é inventado.' },
    ],
  },
  cockpit: {
    paraQueServe: 'A Torre de Controle vista da parede: a tela da TV da sala de suprimentos, sem menu, que se atualiza sozinha.',
    passos: [
      'Abra pelo menu ou pelo "Modo TV" da Torre — a tela abre numa aba nova, já logada.',
      'Na TV, deixe a aba em tela cheia (F11). A tela gira entre a visão geral e cada unidade.',
      'Para ligar uma TV que não tem o sistema aberto, acesse o endereço /cockpit e faça o login uma vez.',
    ],
    regras: [
      { texto: 'Os números são os mesmos da Torre: etapa, atraso, exceção e prazo saem das mesmas funções.' },
      { texto: 'Se uma atualização falhar, os números ficam na tela com um aviso discreto, em vez de apagar o painel.' },
    ],
  },
  'pr-approvals': {
    paraQueServe: 'Onde se decide o que depende da sua alçada: SCs, Nível 1 e Nível 2 das compras e solicitações de material. Cada card traz o que sustenta a decisão.',
    passos: [
      'Cada card mostra quem pediu e por quê, a escolha do comprador contra a proposta mais barata, a justificativa, orçamento, compliance e há quanto tempo espera.',
      'Decida no próprio card: "Aprovar", "Solicitar ajustes" (diga o que ajustar) ou "Rejeitar" (com o motivo).',
      'Para ver o processo inteiro, abra-o pelo número no card.',
      '"Suas decisões recentes" mostra o que você decidiu, do registro que a auditoria lê.',
    ],
    regras: [
      { codigo: 'RFQ-ERR-030', texto: 'No Nível 2, o diretor não pode ser quem escolheu o fornecedor nem quem deu o Nível 1. No Nível 1 não há essa segregação.' },
      { texto: 'A fila mostra só o que você pode decidir — o que não é da sua alçada não aparece aqui. A compra da empresa inteira fica no painel.' },
      { texto: 'A compra do Gestor de Suprimentos não tem Nível 2; a de um comprador vai para o gestor responsável por ele.' },
      { texto: 'Na solicitação de material, cada item mostra o custo de compra e a linha diz o valor do que você está liberando — ele acompanha a quantidade que você ajustar.' },
    ],
  },

  // ------------------------------------------------------------------ Compras
  'pr-new-unit': {
    paraQueServe: 'Abrir uma Solicitação de Compra (SC): o que você precisa, para quando, por quê e em qual centro de custo.',
    passos: [
      'Escolha a finalidade: Compra (segue para aprovação depois da cotação) ou Orçamento (só levantar preço: para depois da cotação e volta para você decidir).',
      'Em cada item, clique em "Buscar no catálogo" e escolha a família ou digite duas letras. As famílias são as ativas do cadastro.',
      'Clique no produto para ver a ficha — foto, código, família, unidade, fornecedores — e use "Usar este produto".',
      'Não achou? Na própria busca, "Pedir item fora do catálogo" abre a descrição livre, já com o que você buscou e com a família do filtro.',
      'Se já sabe que o item não está cadastrado, clique em "Escrever item fora do catálogo" na linha e descreva-o direto, com unidade, quantidade e a família, se souber.',
      'Para produto com grade de tamanhos, informe a quantidade de cada tamanho — cada um vira um item da SC.',
      'Preencha a justificativa, o centro de custo, a data de necessidade e a prioridade.',
      'Em "Mais detalhes" ficam local de entrega, tipo de solicitação, a empresa (da lista de CNPJs cadastrados), orçamento previsto, observação e anexos.',
      'Envie. A SC vai para quem aprova o centro de custo.',
    ],
    regras: [
      { codigo: 'PR-ERR-021', texto: 'Com centros vinculados ao seu cadastro, você só solicita para eles.' },
      { codigo: 'IC-ERR-023', texto: 'EPI/EPC só circula com C.A. válido no par produto-fornecedor — cobrado do tamanho pedido.' },
      { texto: 'Prioridade urgente pede a justificativa da urgência e o impacto de não comprar.' },
      { codigo: 'PR-ERR-023', texto: 'A empresa precisa ser um dos CNPJs ativos do cadastro (Estrutura da Empresa → Empresas).' },
      { codigo: 'PR-ERR-024', texto: 'A finalidade (orçamento ou compra) é obrigatória. O comprador pode corrigi-la na Torre até a SC entrar em cotação (PR-ERR-025).' },
      { codigo: 'PR-ERR-031', texto: 'Cada texto tem limite: a justificativa e a observação, 2.000 caracteres; o motivo e o impacto da urgência, 500; a descrição do item, 500. O campo para de aceitar no limite.' },
    ],
    duvidas: [
      { pergunta: 'Onde a SC vai ser entregue?', resposta: 'No local de entrega escolhido. A lista junta os almoxarifados e os centros de custo que recebem material — quem paga e quem recebe podem ser diferentes.' },
    ],
  },
  'pr-new-multi': {
    paraQueServe: 'Montar uma SC com muitos produtos de uma vez, preenchendo a quantidade direto na lista do catálogo.',
    passos: [
      'Escolha a finalidade: Compra ou Orçamento (obrigatória).',
      'Filtre a lista pela família ou busque pelo nome.',
      'Digite a quantidade nos produtos que você precisa. Só entram na SC os que têm quantidade.',
      'Preencha centro de custo, local de entrega, prioridade, data de necessidade e justificativa.',
      'Gere a SC. Ela segue o mesmo caminho da SC unitária.',
    ],
    regras: [
      { codigo: 'PR-ERR-021', texto: 'Com centros vinculados ao seu cadastro, você só solicita para eles.' },
    ],
  },
  'pr-mine': {
    paraQueServe: 'Acompanhar as suas SCs como uma linha do tempo: com quem está, desde quando e quando chega.',
    passos: [
      'Cada SC mostra seis passos: Enviada → Com o comprador → Em cotação → Aprovação → Pedido fechado → Entregue.',
      'A frase embaixo diz com quem a SC está agora (por exemplo, "Aguardando aprovação de Gustavo — Nível 1").',
      'SC devolvida para ajuste mostra o motivo e os botões "Corrigir" e "Reenviar".',
      'Rascunho ou SC ainda não iniciada pode ser excluída.',
      'Clique na linha (ou no número da SC) para ir aonde ela está: a cotação, a Central de Aprovação, o pedido ou a Torre — conforme a etapa e o que você pode fazer lá. Quem só solicita acompanha pela linha do tempo.',
    ],
    duvidas: [
      { pergunta: 'Minha SC diz que o centro não tem aprovador. E agora?', resposta: 'A fila está parada porque ninguém pode aprovar. Abra um chamado de suporte ou fale com o administrador para cadastrar o aprovador do centro.' },
    ],
  },
  'control-tower': {
    paraQueServe: 'A Torre de Controle: cada item de compra em aberto, em qual etapa está, de quem depende e se está atrasado. É também onde se faz a triagem das SCs.',
    passos: [
      'Os cartões do topo (Novos, Precisa de você, Em cotação, Aguardando aprovação, Em faturamento, Atrasados, Exceções…) abrem exatamente a lista que contaram.',
      'Refine pelos filtros e clique em "Aplicar filtros".',
      'A coluna de ação diz o que falta fazer e leva até lá.',
      'Marque só os itens que vão seguir e clique em "Abrir cotação com os itens marcados": o processo leva só eles, e os demais itens da SC ficam pendentes nela, na etapa de Solicitação, para seguir depois.',
      'Para atribuir: marque os itens e escolha em "Atribuir as SCs marcadas a". A atribuição vale para a SC inteira de cada item marcado.',
      'A prioridade se muda na própria linha, com o impacto de não comprar.',
      '"Modo TV" abre o cockpit numa aba nova.',
    ],
    regras: [
      { texto: '"Atrasado" é sobre a data prometida ao solicitante; "prazo estourado" é sobre o tempo da etapa. São perguntas diferentes.' },
      { texto: 'Cada item mostra o processo em que ele está: o item que ficou para trás aparece na Solicitação, e não no processo dos outros itens da mesma SC.' },
      { codigo: 'RFQ-ERR-061', texto: 'Só itens do mesmo centro de custo entram na mesma cotação.' },
      { codigo: 'RFQ-ERR-063', texto: 'Orçamento e compra não se misturam no mesmo processo.' },
      { texto: '"Em faturamento" espera o fornecedor (O.C. sem nota fiscal); "Aguardando recebimento" espera o almoxarifado (nota lançada, material não recebido).' },
      { texto: 'O prazo da etapa mede e avisa, nunca bloqueia. A atenção chega a 80% do prazo.' },
      { texto: 'A coluna Comprador mostra quem conduziu a compra (escolheu o vencedor ou abriu a cotação). Antes da cotação, mostra o responsável da triagem. O aprovador nunca aparece ali.' },
      { texto: 'A marca "Orçamento" diz que a SC é só levantamento de preço. Antes de cotar, "é compra"/"é orçamento" na linha corrige a finalidade. O orçamento apresentado espera a decisão de quem pediu e não conta no prazo da etapa.' },
    ],
  },
  'rfq-queue': {
    paraQueServe: 'As SCs aprovadas que aguardam cotação, e a abertura do processo de cotação.',
    passos: [
      'Marque as SCs que entram no mesmo processo.',
      'Se houver famílias diferentes, a tela oferece separar por família.',
      'Clique em "Abrir processo". O processo nasce aberto para convidar fornecedores.',
    ],
  },
  quotations: {
    paraQueServe: 'A lista dos processos de cotação, com a etapa de cada um.',
    passos: [
      'Filtre pela situação ou busque pelo número.',
      'Abra o processo pelo número para convidar, registrar propostas, escolher e acompanhar as aprovações.',
    ],
  },
  'quotation-detail': {
    paraQueServe: 'O processo de cotação inteiro: convidados, propostas, mapa de cotação, escolha do vencedor, aprovações e os pedidos que nasceram dele.',
    passos: [
      'Convide fornecedores. O pré-cadastro pede só razão social e telefone; CNPJ é opcional.',
      'Registre as propostas (preço, frete, prazo de entrega, pagamento, validade). Contrato de parceria vigente preenche o preço sozinho.',
      'Clique em "Encerrar para análise" quando as propostas estiverem no mapa.',
      'Item marcado "fora do catálogo" precisa virar produto antes da escolha: clique em "Cadastrar produto" no item, busque se ele já existe ou cadastre o novo ali mesmo.',
      'Na grade, escolha o vencedor de cada item e escreva a justificativa. A grade marca o menor preço e quanto cada oferta está acima dele — mas não escolhe por você.',
      'Confirmada a escolha, o processo segue para as aprovações. O Nível 1 e o Nível 2 são dados na Central de Aprovação — o card "Próximo passo" leva até lá.',
      'Aprovado o Nível 2, nasce um pedido por fornecedor: a O.C. do ERP se registra na tela do pedido.',
      'Processo de orçamento: depois da escolha ele para em "Orçamento apresentado". Se o solicitante decidir comprar, use "Converter em compra e enviar ao Nível 1".',
    ],
    regras: [
      { codigo: 'RFQ-ERR-020', texto: 'Proposta depois do prazo do convite é barrada. Saídas: dar novo prazo ou seguir sem o fornecedor (com motivo).' },
      { codigo: 'RFQ-ERR-023', texto: 'Todo item precisa de um vencedor.' },
      { codigo: 'RFQ-ERR-026', texto: 'Item fora do catálogo cota, mas não é comprado: ele vira produto antes da escolha do vencedor. O orçamento fecha com item digitado e é cobrado ao virar compra.' },
      { codigo: 'RFQ-ERR-027', texto: 'O produto do item se define até a escolha do vencedor, uma vez só.' },
      { codigo: 'RFQ-ERR-063', texto: 'Orçamento e compra não se misturam no mesmo processo.' },
      { codigo: 'RFQ-ERR-064', texto: 'Só o orçamento apresentado vira compra — e o Nível 1 vê a marca "Nasceu como orçamento".' },
      { codigo: 'RFQ-ERR-025', texto: 'Quando a quantidade de um item é dividida entre fornecedores, a soma precisa fechar a quantidade pedida.' },
      { codigo: 'SUP-ERR-030', texto: 'Só fornecedor homologado vence o BID. Homologar exige o CNPJ.' },
      { texto: '"Preencher com o melhor preço" e "levar tudo" são pontos de partida, não decisões — revise antes de enviar.' },
      { texto: 'O score multicritério informa e nunca decide: a escolha é do comprador, com justificativa.' },
    ],
  },
  'buy-orders': {
    paraQueServe: 'Os pedidos de compra, criados na aprovação do Nível 2 — um por fornecedor —, com a situação de O.C., faturamento e entrega.',
    passos: [
      'Busque pelo número do pedido, da O.C. do ERP ou pelo fornecedor.',
      'Abra o pedido para registrar a O.C., as notas fiscais e a entrega.',
      '"PDF" gera o documento do pedido.',
    ],
  },
  'order-detail': {
    paraQueServe: 'Tudo o que acontece depois da aprovação, numa linha do tempo de três passos: 1 O.C. → 2 Faturamento → 3 Entrega.',
    passos: [
      '1. Registre a O.C. fechada no ERP SENIOR: número, data e anexo. Ela pode cobrir o pedido inteiro ou parte dos itens.',
      '2. Lance as notas fiscais do fornecedor, com o valor de cada uma. Podem ser várias, desde que a soma não passe do total aprovado — a tela mostra o saldo a faturar.',
      '3. Confirme a entrega com "Registrar entrega", ou "Encerrar saldo" quando o restante não vai chegar.',
    ],
    regras: [
      { codigo: 'RFQ-ERR-040', texto: 'A O.C. nunca é emitida pelo sistema: ela é fechada no ERP e aqui só se registra o número.' },
      { codigo: 'PO-ERR-054', texto: 'Sem O.C. do ERP, a compra não fecha — a não ser com a observação dizendo por quê (mínimo de 10 caracteres). O número nunca é inventado.' },
      { codigo: 'PO-ERR-059', texto: 'A soma das O.C.s não passa do pedido; a mensagem diz quanto falta.' },
      { codigo: 'PO-ERR-060', texto: 'A soma das notas fiscais não passa do total aprovado no Nível 2: o aprovado é o teto do que se paga. A mensagem diz o aprovado, o faturado e o saldo.' },
      { codigo: 'PO-ERR-053', texto: 'Toda nota precisa do valor — é ele que abate o saldo.' },
      { texto: 'O cabeçalho diz de onde sai o total aprovado: itens, frete, impostos e desconto da proposta. Item a 1.900 com total a 1.800 é desconto, não erro.' },
    ],
  },
  contracts: {
    paraQueServe: 'Os contratos de parceria: cadastrar, editar, acompanhar teto, consumo e saldo, e registrar reajustes.',
    passos: [
      '"Novo contrato de parceria": escolha o fornecedor (ativos e sem contrato) e preencha número, vigência, teto e os produtos.',
      'Em "Buscar produto no catálogo…", escolha a família ou digite ao menos duas letras do código ou da descrição.',
      'Em cada produto, o preço fixo, o prazo de entrega e a condição de pagamento que valem enquanto o contrato durar.',
      'Para mudar um contrato, use "Editar contrato" na linha dele.',
      'Veja o teto contratado, o consumido na vigência e o saldo de cada contrato.',
      'Para um reajuste, registre o percentual pleiteado e o fechado com "Registrar reajuste".',
      'Clique no nome do fornecedor para abrir a ficha do contrato: documentos, compras e histórico.',
    ],
    regras: [
      { texto: 'O contrato de parceria é mantido aqui. O cadastro do fornecedor continua em Cadastros → Fornecedores.' },
      { texto: 'Preço de contrato só preenche a proposta dentro da vigência — preço vencido não entra calado.' },
      { codigo: 'SUP-ERR-020', texto: 'A vigência não pode terminar antes de começar.' },
      { texto: 'Salvar sem nenhum produto encerra o contrato: o fornecedor volta a ser cotado normalmente.' },
      { texto: 'Quem já tem contrato não aparece em "Novo contrato": edite pela linha dele.' },
      { texto: 'O produto se escolhe buscando, não rolando: o catálogo tem milhares de itens.' },
      { texto: 'Produto com tamanho entra uma linha por tamanho — a bota 38 e a 39 têm preço próprio.' },
    ],
  },
  'contract-detail': {
    paraQueServe: 'Tudo sobre o contrato de um fornecedor numa tela: os documentos, os produtos com preço e prazo, as compras feitas e o histórico do contrato.',
    passos: [
      'No topo, veja a vigência, o teto, o consumido, o saldo e o custo evitado em reajustes.',
      'Em Documentos, abra o contrato assinado, os aditivos e as certidões. "Anexar documento do contrato" guarda o contrato assinado ou um aditivo.',
      'Em Compras, cada pedido diz se abate o saldo: só conta o emitido dentro da vigência e não cancelado.',
      'O Histórico conta o que mudou no contrato — vigência, teto, preço de cada produto, documentos e reajustes —, com quem e quando.',
    ],
    regras: [
      { texto: 'O contrato assinado e o aditivo não são certidão: a validade deles é a vigência, e o contrato vencer não restringe a homologação do fornecedor.' },
      { texto: 'Contrato cadastrado antes de o sistema registrar a história começa na primeira alteração registrada, e a tela avisa.' },
      { texto: 'Salvar o contrato sem mudar nada não entra no histórico.' },
    ],
  },
  scorecard: {
    paraQueServe: 'O desempenho de cada fornecedor: entrega no prazo e completa (OTIF) e o histórico de pedidos.',
    passos: [
      'Compare os fornecedores pelo OTIF e pelo volume.',
      'Use o scorecard para decidir quem convidar na próxima cotação.',
    ],
    regras: [
      { texto: 'Sem entrega medida, o OTIF é traço, não zero.' },
    ],
  },

  // ------------------------------------------------------------------ Material
  'mr-new': {
    paraQueServe: 'Pedir material ao almoxarifado, para um centro de custo.',
    passos: [
      'Escolha o centro de custo.',
      'Escolha a família ou busque o produto pelo nome ou pelo código — as duas portas levam à mesma lista.',
      'Marque os produtos, informe as quantidades e, se precisar, uma observação.',
      'Clique em "Enviar ao almoxarifado".',
    ],
    regras: [
      { texto: 'Com centros vinculados ao seu cadastro, você só pede material para eles.' },
      { texto: 'A lista traz só o que é de almoxarifado: quem decide é a família, no cadastro de '
          + 'Famílias de Produtos, e o produto ajusta quando é exceção. Produto de uma família que '
          + 'não é de almoxarifado não aparece no seletor de família — se ele for exceção, ache-o pela busca.' },
      { texto: 'EPI e EPC sem C.A. em nenhum fornecedor não podem ser solicitados (IC-ERR-023).' },
    ],
  },
  'mr-mine': {
    paraQueServe: 'Acompanhar as suas solicitações de material: aprovação, atendimento, o que ficou parcial e quanto vale.',
    passos: [
      'Veja a situação de cada solicitação.',
      'A coluna "Valor" é o que você pediu, pelo custo de compra do produto no dia do pedido; quando o centro liberou menos, a linha diz o valor liberado.',
      'Solicitação ainda não atendida pode ser cancelada, com o motivo.',
    ],
    regras: [
      { texto: 'O custo é congelado no pedido: mudar o preço do produto depois não muda a solicitação. "Sem custo cadastrado" quer dizer que o produto ainda não tem custo de compra, não que vale zero.' },
    ],
  },
  triage: {
    paraQueServe: 'A triagem das solicitações de material: quem atende cada uma e há quanto tempo esperam.',
    passos: [
      'Filtre por situação, solicitante ou tempo na fila.',
      'Atribua o responsável e, se necessário, mude a prioridade com o impacto de não atender.',
    ],
    regras: [
      { texto: 'A triagem de compra (SC) fica na Torre de Controle; esta tela é só de material.' },
    ],
  },
  'wh-queue': {
    paraQueServe: 'A fila do almoxarifado: as solicitações de material aprovadas, prontas para atender.',
    passos: [
      'Clique em "Atender" na solicitação.',
      'Informe o que está sendo entregue de cada item, ou use "Atender tudo" — o campo já vem com o que ainda falta.',
      'Decida as duas coisas: concluir o atendimento ou deixar a solicitação na fila, e comprar ou não o que faltou.',
      'Clique no botão (ele diz o que vai fazer: "Confirmar atendimento" ou "Registrar entrega parcial").',
    ],
    regras: [
      { texto: 'Desmarcando "Concluir o atendimento", a solicitação continua na fila com o que já '
          + 'saiu registrado — é o caso do resto da carga que chega depois. A próxima entrega soma à primeira.' },
      { texto: 'Desmarcando "Comprar o que faltou", nenhuma solicitação de compra é aberta: use quando '
          + 'o material já está a caminho. A SC do faltante sai uma vez só por solicitação.' },
      { texto: 'Concluir sem entregar nada é decisão válida — quer dizer que não havia nada em estoque.' },
    ],
  },
  'wh-panel': {
    paraQueServe: 'O painel de atendimentos do almoxarifado: aguardando aprovação, em andamento, concluídos e parciais aguardando compra.',
    passos: [
      'Leia os cartões do topo para ver onde está o volume.',
      'Clique num cartão para ver só a lista por trás daquele número.',
      'Use o filtro do topo para recortar por centro de custo, solicitante ou data de criação.',
      'Nas quebras por centro de custo e por solicitante, clique na linha para filtrar por ela — e clique de novo para tirar o filtro.',
    ],
    regras: [
      { texto: 'O filtro vale para os cartões e para as listas ao mesmo tempo: o número do topo sempre conta exatamente a lista que ele abre.' },
      { texto: 'A quebra por solicitante é por pessoa, não por nome: dois homônimos continuam sendo duas linhas.' },
      { texto: 'A data recorta pela criação da solicitação, e os dois dias informados entram inteiros.' },
      { texto: 'A coluna "Valor" é o valor liberado pelo centro (o pedido, enquanto ele não decide), pelo custo de compra congelado no dia do pedido.' },
    ],
  },

  // ------------------------------------------------------------------ Melhoria
  'acao-plano': {
    paraQueServe: 'Os planos de ação: o projeto (problema, porquê, responsáveis, riscos, dinheiro) e, dentro dele, as ações com dono e prazo.',
    passos: [
      'Clique em "Abrir um plano" e descreva o problema e o que o plano vai resolver.',
      'Filtre pela situação: pendentes, em andamento, atrasados, concluídos, encerrados.',
      'Abra o plano pelo código para ver as cinco abas.',
    ],
    regras: [
      { texto: 'Toda ação pertence a um plano.' },
      { texto: 'Ação suspensa não conta como atrasada: ela está parada por decisão.' },
      { texto: 'O plano de um ciclo de melhoria só aparece para quem enxerga o ciclo.' },
    ],
  },
  'plan-detail': {
    paraQueServe: 'Um plano de ação por inteiro, em cinco abas: Ações, Estratégico, Riscos, Causa raiz e Lições.',
    passos: [
      'Ações: cada uma com o quê, quem, quando, como e a causa que ela ataca.',
      'Estratégico: patrocinador, KPI afetado, investimento e saving esperado e realizado.',
      'Riscos: probabilidade × impacto — a severidade é calculada, não escolhida.',
      'Causa raiz e Lições: o que se descobriu e o que se aprendeu.',
      'Use "Encerrar o plano" quando ele acabar, com a evidência.',
    ],
    regras: [
      { codigo: 'AC-ERR-014', texto: 'Suspender ou cancelar uma ação exige o motivo.' },
      { codigo: 'AP-ERR-020', texto: 'Plano encerrado não se edita. Reabrir é do gestor ou do administrador.' },
      { codigo: 'AP-ERR-902', texto: 'Enxergar não é editar: quem conduz o plano (quem criou, os responsáveis, o gestor, o administrador e quem conduz o ciclo) é que mexe nele. Quem responde por uma ação atualiza o avanço dela.' },
      { texto: 'Sem investimento, o ROI fica vazio — não zero.' },
      { texto: 'Encerrar é decisão: um plano a 100% continua ativo até alguém encerrá-lo.' },
    ],
  },
  pdca: {
    paraQueServe: 'Os ciclos de melhoria (PDCA): onde se trata a causa de um problema, e não só a tarefa.',
    passos: [
      'Clique em "Abrir um ciclo": título, problema, escopo e meta.',
      'Filtre pela fase (Plan, Do, Check, Act, Encerrado) ou busque.',
      'Abra o ciclo para usar as ferramentas de análise e ligar as ações.',
    ],
    regras: [
      { texto: 'Você vê os ciclos que criou, de que é dono, em que foi marcado, em que responde por ação e os do seu setor.' },
    ],
  },
  'pdca-detail': {
    paraQueServe: 'Um ciclo de melhoria: Plan (problema, causa e meta), Do (as ações), Check e Act, com as ferramentas de análise e a leitura automática.',
    passos: [
      'Plan: descreva o problema e a meta. Use as ferramentas — 5 Porquês, Ishikawa, Pareto, GUT, Brainstorming, 5W2H, Kaizen, Fluxograma.',
      'Do: ligue as ações que atacam a causa vital.',
      'Check: registre o resultado medido.',
      'Act: padronize o que funcionou e anote as lições.',
      'A leitura automática aponta, por exemplo, causa vital sem ação.',
      'Gere o A3 para imprimir ou apresentar.',
    ],
    regras: [
      { codigo: 'PDCA-ERR-043', texto: 'Encerrar exige dizer se a meta foi atingida.' },
      { codigo: 'PDCA-ERR-040', texto: 'Encerrar exige o motivo em texto.' },
      { codigo: 'PDCA-ERR-041', texto: 'Com ação em aberto, encerrar pede a confirmação explícita, com a lista do que sobrou.' },
      { codigo: 'PDCA-ERR-044', texto: 'A fase "encerrado" não se escolhe pela edição: use "Encerrar o ciclo".' },
      { texto: 'Uma ferramenta de cada tipo por ciclo. O Pareto e o GUT são calculados pelo servidor.' },
    ],
  },

  // ------------------------------------------------------------------ Cadastros
  products: {
    paraQueServe: 'O catálogo de produtos: código, descrição, família, unidade, preço de referência, foto e os fornecedores de cada produto (com o C.A. de EPI).',
    passos: [
      'Busque ou filtre por família.',
      '"Novo produto" cadastra um item. Com "Grade de tamanhos", cadastra todos os tamanhos de uma vez.',
      'Em fornecedores do produto, registre o código no fornecedor e o C.A. (para EPI/EPC).',
      '"Importar produtos por planilha" carrega em volume (gestor de suprimentos e administrador).',
      '"Excluir" apaga de vez o produto cadastrado por engano; o que já circulou se inativa (gestor de suprimentos e administrador).',
    ],
    regras: [
      { codigo: 'IC-ERR-018', texto: 'Produto de almoxarifado nasce e se corrige com o custo de compra (o "Custo de compra (R$)" do formulário): é ele que dá valor à solicitação de material. Vale para a família de almoxarifado e para o produto marcado "sempre entra"; o aviso do topo abre a lista dos que ainda estão sem custo.' },
      { codigo: 'IC-ERR-030', texto: 'Só se exclui o produto que nunca entrou numa SC, cotação, pedido, contrato, solicitação de material ou no estoque — a mensagem diz onde ele foi usado. Nesses casos, inative.' },
      { texto: 'O comprador cadastra e corrige produtos; importar, excluir e manter famílias continuam com o gestor de suprimentos e o administrador.' },
      { texto: 'Cada tamanho é um produto próprio (código 12003-38, 12003-39…). A grade é cadastrada inteira ou nada.' },
      { codigo: 'IC-ERR-023', texto: 'EPI/EPC precisa de C.A. válido no par produto-fornecedor para circular.' },
    ],
  },
  families: {
    paraQueServe: 'As famílias de produtos e a meta de prazo de cada etapa (solicitação → cotação → aprovação → O.C. → entrega).',
    passos: [
      '"Nova família": nome, categoria e as metas de prazo.',
      'As metas alimentam o painel "Prazos do processo — meta da família × realizado".',
      'As famílias ativas são as que aparecem na busca de produto da SC, da Solicitação em Lote e do material.',
    ],
    regras: [
      { codigo: 'IC-ERR-031', texto: 'Só se exclui a família sem nenhum produto, contando os inativos. Com produto dentro, mova-os para outra família ou inative a família.' },
    ],
  },
  'cost-centers': {
    paraQueServe: 'Os centros de custo: onde o dinheiro cai, quem aprova e até quanto, e se o centro recebe material.',
    passos: [
      '"Novo centro de custo": nome, regional, cliente, CNPJ de compras.',
      'Defina o gerente responsável e os limites de Nível 1 e Nível 2.',
      'Marque se o centro recebe material — só então ele aparece como local de entrega.',
    ],
    regras: [
      { texto: 'Centro sem aprovador cadastrado para o fluxo: a Torre e a SC dizem isso na linha.' },
    ],
  },
  company: {
    paraQueServe: 'Os CNPJs do grupo e o padrão da O.C.: cabeçalho, endereço de entrega, cláusulas e política de pagamento.',
    passos: [
      '"Novo CNPJ" cadastra uma empresa do grupo.',
      'Em "Padrão da O.C.", preencha os dados que saem no PDF do pedido e clique em "Salvar dados da empresa".',
    ],
    regras: [
      { codigo: 'IAM-ERR-018', texto: 'Só o administrador mantém o cadastro da empresa.' },
    ],
  },
  sectors: {
    paraQueServe: 'Os setores — quem trabalha (RH, TI, Manutenção). Não é centro de custo, que é onde o dinheiro cai.',
    passos: [
      '"Novo setor": nome (e código, se quiser; senão sai do nome).',
      'Setor fora de uso se inativa — nunca se apaga.',
    ],
    regras: [
      { codigo: 'SET-ERR-012', texto: 'O código é único e não muda depois de gravado.' },
      { codigo: 'IAM-ERR-023', texto: 'Setor inativo não se vincula a usuário.' },
    ],
  },
  users: {
    paraQueServe: 'Os usuários do sistema: papel, módulos liberados, centros vinculados, setor e responsáveis.',
    passos: [
      '"Novo usuário": nome, e-mail, papel e setor.',
      'Marque os módulos. Ao escolher o papel, o sistema sugere os módulos padrão dele.',
      'Vincule os centros de custo quando a pessoa só deve solicitar para eles. Para o comprador, defina o gestor responsável.',
      '"Nova senha" redefine a senha — ela nasce provisória.',
    ],
    regras: [
      { codigo: 'IAM-ERR-022', texto: 'Senha definida por outra pessoa é provisória: no primeiro acesso, o usuário precisa trocá-la.' },
      { texto: 'Plano de Ação, PDCA e Atender chamados de suporte não vêm em padrão de papel nenhum: são dados a quem vai usá-los.' },
      { texto: 'Módulo novo passa a valer no próximo login do usuário.' },
    ],
  },
  announcements: {
    paraQueServe: 'Os comunicados que aparecem para todos ao abrir o sistema.',
    passos: [
      '"Novo comunicado": título, mensagem, imagem (opcional) e a vigência.',
      'Clique em "Publicar comunicado". Ele aparece por cima de qualquer tela durante a vigência.',
    ],
  },
  suppliers: {
    paraQueServe: 'O cadastro de fornecedores: dados, homologação, documentos e a chave do Portal do Fornecedor.',
    passos: [
      '"Novo fornecedor": razão social e telefone bastam para o pré-cadastro.',
      'Para homologar, informe o CNPJ e anexe os documentos.',
      '"Chave do Portal do Fornecedor" gera o acesso para o fornecedor responder cotações.',
      'A coluna Contrato diz se o fornecedor tem um e leva à ficha dele.',
    ],
    regras: [
      { texto: 'O contrato de parceria é mantido em Compras → Contratos, não aqui.' },
      { codigo: 'SUP-ERR-013', texto: 'Homologar exige CPF/CNPJ.' },
      { codigo: 'SUP-ERR-015', texto: 'Não se cadastra dois fornecedores com a mesma razão social (sem acento, caixa ou pontuação).' },
      { texto: 'O CNPJ, uma vez gravado, não se troca. Fundir dois cadastros não existe: inative o repetido.' },
    ],
  },
  'indicator-goals': {
    paraQueServe: 'As metas mensais dos indicadores que o painel e a diretoria comparam: valor comprado, saving, OTIF, prazos e SCs em atraso.',
    passos: [
      'Digite a meta de um mês em cada indicador que tem meta. Deixe vazio o que não tem.',
      'Clique em "Salvar metas". Os cards passam a mostrar a meta do período e quanto dela foi atingido.',
    ],
    regras: [
      { texto: 'Sem meta cadastrada, o card mostra só o número — nunca uma meta inventada.' },
      { texto: 'O que soma (valor comprado, saving) multiplica a meta pelos meses do período; o que é média ou taxa usa a mesma meta.' },
      { texto: 'Verde bate a meta; amarelo está perto (até 20% longe dela); vermelho está longe.' },
      { codigo: 'MET-ERR-011', texto: 'A meta precisa ser maior que zero. Para tirá-la, deixe o campo vazio.' },
    ],
  },
  'score-weights': {
    paraQueServe: 'Os pesos do score multicritério das cotações: preço, entrega, pagamento, OTIF e risco.',
    passos: [
      'Ajuste os pesos e clique em "Salvar pesos".',
      'A régua salva é a mesma que aparece na tela do processo e entra na conta.',
    ],
    regras: [
      { codigo: 'SCR-ERR-010', texto: 'Os pesos somam 100.' },
      { texto: 'Peso 0 desliga o critério. Critério sem dado sai da conta — fornecedor novo não é punido por ser novo.' },
    ],
  },
  'request-types': {
    paraQueServe: 'Os tipos de solicitação (EPI, Manutenção…), que definem o prazo de cada etapa quando precisam de um diferente do padrão.',
    passos: [
      'Cadastre o tipo com código e nome.',
      'Tipo fora de uso se inativa, nunca se apaga.',
    ],
    regras: [
      { texto: 'O código é identidade e não muda depois de gravado.' },
    ],
  },
  'stage-sla': {
    paraQueServe: 'O prazo de cada etapa da compra, no padrão e por tipo de solicitação.',
    passos: [
      'Ajuste os dias de cada etapa no conjunto padrão.',
      'Para um tipo, defina só a etapa que muda — as outras herdam do padrão.',
    ],
    regras: [
      { texto: 'Zero desliga a cobrança da etapa.' },
      { texto: 'Voltar a herdar apaga a exceção do tipo, em vez de copiar o número.' },
      { texto: 'O prazo mede e avisa, nunca bloqueia.' },
    ],
  },
  'material-sla': {
    paraQueServe: 'Quanto tempo o almoxarifado tem para atender, no padrão e por família de produto.',
    passos: [
      'Ajuste o padrão: ele vale para toda família que não definir o seu.',
      'Defina só a família que foge do padrão — as outras herdam.',
      'Use "voltar ao padrão" para desfazer uma exceção.',
    ],
    regras: [
      { texto: 'O relógio começa na liberação do Nível 1, não na criação da solicitação.' },
      { texto: 'A solicitação responde pelo prazo mais curto entre as famílias dos itens dela.' },
      { texto: 'Zero desliga a cobrança da família, e a família em zero sai da conta da solicitação.' },
      { texto: 'Voltar a herdar apaga a exceção, em vez de copiar o número do padrão.' },
      { texto: 'O prazo mede e avisa, nunca bloqueia o atendimento.' },
      { texto: 'Não é o prazo-meta da família, que é do processo de compra.' },
    ],
  },
  'payment-methods': {
    paraQueServe: 'As formas de pagamento aceitas nas propostas (boleto, PIX, depósito…).',
    passos: ['"Nova forma de pagamento": nome.', 'Ela passa a aparecer na proposta da cotação.'],
  },
  'payment-terms': {
    paraQueServe: 'As condições de pagamento: número de parcelas e prazo da primeira.',
    passos: ['"Nova condição de pagamento": nome, parcelas e prazo da 1ª parcela.', 'Ela alimenta o prazo médio de pagamento (DPO) dos relatórios.'],
  },

  // ------------------------------------------------------------------ Suporte
  support: {
    paraQueServe: 'Os seus chamados de suporte e, para quem atende, a fila de todos.',
    passos: [
      'Abra um chamado pelo botão "Suporte" no topo de qualquer tela — a tela onde você está já vai junto.',
      'A situação diz de quem é a vez: "Aguardando o suporte" ou "Aguardando você".',
      'Clique no chamado para ler a conversa e responder.',
    ],
    regras: [
      { texto: 'Abrir chamado não depende de módulo: qualquer usuário pede ajuda.' },
      { texto: 'Atender é do administrador e de quem recebeu o módulo "Atender chamados de suporte".' },
    ],
  },
  'support-detail': {
    paraQueServe: 'A conversa de um chamado, na ordem em que aconteceu.',
    passos: [
      'Leia a descrição e as respostas.',
      'Responda no campo embaixo; anexe um print se ajudar.',
      'Quando estiver resolvido, clique em "Encerrar chamado".',
    ],
    regras: [
      { codigo: 'CH-ERR-021', texto: 'O suporte só resolve dizendo o que foi feito.' },
      { texto: 'Responder um chamado resolvido o reabre — não é preciso abrir outro.' },
    ],
  },
};
