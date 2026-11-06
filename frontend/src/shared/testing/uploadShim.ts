// Upload multipart no jsdom + MSW: o File/FormData do jsdom nao atravessam o fetch do Node (corpo "[object FormData]"
// e "content-length mismatch") e o parser do undici so aceita o File do Node em request.formData().
// Usamos File/FormData do Node e serializamos o FormData em bytes antes do fetch interceptado pelo MSW.
// Ver T-117 (AvatarDialog). Confinado aos testes que enviam arquivo (R-22): `installUploadShim()`
// troca os globais e devolve a funcao que os restaura; nao fica no setup global.

type GlobalName = "File" | "FormData" | "fetch";

function define(name: GlobalName, descriptor: PropertyDescriptor): void {
  Object.defineProperty(globalThis, name, { configurable: true, ...descriptor });
}

export async function installUploadShim(): Promise<() => void> {
  const originals = {
    File: Object.getOwnPropertyDescriptor(globalThis, "File"),
    FormData: Object.getOwnPropertyDescriptor(globalThis, "FormData"),
    fetch: Object.getOwnPropertyDescriptor(globalThis, "fetch"),
  };

  const NodeFormData = (
    await new Response("a=1", {
      headers: { "content-type": "application/x-www-form-urlencoded" },
    }).formData()
  ).constructor as typeof FormData;
  const { File: NodeFile } = await import("node:buffer");
  define("File", { value: NodeFile, writable: true });
  // `new FormData(form)` (leitura de formularios) segue no jsdom; `new FormData()` vazio e o do Node.
  const JsdomFormData = globalThis.FormData;
  define("FormData", {
    value: new Proxy(NodeFormData, {
      construct: (target, args) =>
        args.length > 0 ? Reflect.construct(JsdomFormData, args) : Reflect.construct(target, args),
    }),
    writable: true,
  });

  let patchedFetch: typeof fetch = globalThis.fetch;
  const serializingFetch: typeof fetch = async (input, init) => {
    if (init?.body instanceof NodeFormData) {
      const encoded = new Response(init.body);
      const headers = new Headers(init.headers);
      headers.set("content-type", encoded.headers.get("content-type") ?? "");
      return patchedFetch(input, {
        ...init,
        headers,
        body: new Uint8Array(await encoded.arrayBuffer()),
      });
    }
    return patchedFetch(input, init);
  };
  define("fetch", {
    get: () => serializingFetch,
    set: (value: typeof fetch) => {
      patchedFetch = value;
    },
  });

  return () => {
    for (const name of ["File", "FormData", "fetch"] as const) {
      const original = originals[name];
      if (original) Object.defineProperty(globalThis, name, original);
      else delete (globalThis as Record<string, unknown>)[name];
    }
  };
}
