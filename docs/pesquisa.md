# Pesquisa pública · 2 de novembro de 2026

Este levantamento procura assuntos para uma demonstração independente. Não é auditoria das empresas, incidente descoberto, diagnóstico dos sistemas atuais nem evidência de que uma equipe precisa deste código.

## PicPay

Fonte primária: https://developers-business.picpay.com/wallet/en/one-click/guides/idempotency-key

O contrato público exige `x-Idempotency-Key` para repetição segura de certas operações e descreve o tratamento de resultados já processados e em processamento. Isso é um requisito publicado da API, não relato de um incidente. A demo implementa um contrato próprio e não acessa sandbox nem produção.

**Abordagem:** apresentar um experimento de reenvio concorrente e confirmação perdida; perguntar se faz sentido para uma conversa sobre oportunidades backend/full-stack. Não escrever “resolvi um problema do PicPay”.

## CI&T

Fonte primária indexada: https://ciandt.com/uk/en-gb/case-study/transforming-top-30-global-bank-its-digital-core

O case relata um cliente bancário, demora entre concepção e entrega e gargalos como repasses entre equipes e espera. Também discute modernização, automação de testes e DevOps. É um case histórico; a página não comprova que o problema continua presente. O acesso direto retornou 403 na pesquisa; o texto indexado da página foi a base disponível.

A conexão com RetryLab é limitada: entregar um componente pequeno com testes de falhas e decisões registradas. A solução organizacional do case não é uma fila de eventos.

Canal de dúvidas de recrutamento divulgado pela própria empresa: `attractingtalk@ciandt.com`, em https://ciandt.com/us/en-us/careers/open-positions . O canal é para dúvidas do processo e dados de candidatura; não é promessa de avaliação técnica espontânea. Abordagem apropriada: perguntar como acrescentar o projeto à candidatura já existente.

## Stefanini

Fonte primária: https://stefanini.com/pt/newsroom/conheca-os-projetos-de-cloud-que-a-stefanini-technology-desenvolveu-para-diferentes-industrias/

A publicação, datada de 10/07/2026 na página consultada, descreve clientes com provisionamento manual, incidentes de configuração e instabilidade. Relata automação e monitoramento como partes da solução. São problemas de clientes dos cases, não afirmações de defeitos na infraestrutura interna da Stefanini.

RetryLab ilustra recuperação e visibilidade do estado. Não implementa infraestrutura como código, plataforma cloud ou as soluções descritas no case.

## Empregabilidade e remuneração

Foram encontrados canais oficiais de carreiras da CI&T e PicPay. Isso não comprova chance individual de contratação, vaga compatível disponível nem que uma empresa paga bem para todo cargo. A remuneração precisa ser comparada por função, nível, localidade e regime quando houver uma vaga concreta. Não foi usado ranking salarial para justificar o projeto.

## Critério para contato

Usar contatos públicos de recrutamento ou uma conversa já existente; não adivinhar endereços nem procurar e-mails particulares. Não usar contatos de imprensa, jurídico ou privacidade para pedir uma vaga. Contatos encontrados apenas em anúncios antigos não foram considerados verificados para envio nesta rodada.
