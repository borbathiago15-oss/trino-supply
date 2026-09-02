import { baixar } from './cliente';

export const baixarDocumento = (id: string) => baixar('/api/v1/documents/' + id, 'Falha ao baixar o documento.');
