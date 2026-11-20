// Tipos minimos do gltf-validator (Khronos, sem .d.ts proprio), so o que os testes usam.
declare module "gltf-validator" {
  export interface ValidatorMessage {
    readonly code: string;
    readonly message: string;
    readonly severity: number;
    readonly pointer?: string;
  }
  export interface ValidatorReport {
    readonly issues: {
      readonly numErrors: number;
      readonly messages: readonly ValidatorMessage[];
    };
  }
  export function validateBytes(
    data: Uint8Array,
    options?: { readonly uri?: string; readonly maxIssues?: number },
  ): Promise<ValidatorReport>;
}
