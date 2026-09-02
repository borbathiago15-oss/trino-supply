import { baixar } from './cliente';

export const baixarDocumento = (id: string) => baixar('/api/v1/documents/' + id, 'Falha ao baixar o documento.');

/**
 * Documentos exigem token, então uma <img src> direta não funciona: o arquivo
 * é baixado e vira uma URL de blob. O cache evita rebaixar a mesma foto a cada
 * renderização da lista.
 */
const cache = new Map<string, Promise<string>>();

export function urlDocumento(id: string): Promise<string> {
  let url = cache.get(id);
  if (!url) {
    url = baixarDocumento(id).then((blob) => URL.createObjectURL(blob));
    url.catch(() => cache.delete(id));
    cache.set(id, url);
  }
  return url;
}
