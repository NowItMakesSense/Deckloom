// T-L01C / REQ-L03 v5: o botao de acao principal de cada tela da conta leva icone. O comportamento nao muda
// (o icone e decorativo), entao os testes de cada tela seguem valendo.
import { describe, expect, it } from "vitest";

const sources = import.meta.glob("./*Page.tsx", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

const MAIN_ACTION: Array<[string, RegExp]> = [
  ["./LoginPage.tsx", /<Button[^>]*type="submit"[^>]*icon=\{<LogInIcon/],
  ["./CadastroPage.tsx", /<Button[^>]*type="submit"[^>]*icon=\{<UserPlusIcon/],
  ["./ForgotPasswordPage.tsx", /<Button[^>]*type="submit"[^>]*icon=\{<ArrowRightIcon/],
  ["./ResetPasswordPage.tsx", /<Button[^>]*type="submit"[^>]*icon=\{<CheckIcon/],
  ["./GoogleLinkPage.tsx", /<Button[^>]*type="submit"[^>]*icon=\{<CheckIcon/],
  ["./GoogleSignupPage.tsx", /<Button[^>]*type="submit"[^>]*icon=\{<UserPlusIcon/],
  ["./EditProfilePage.tsx", /<Button[^>]*type="submit"[^>]*icon=\{<CheckIcon/],
  ["./ProfilePage.tsx", /<Button[^>]*variant="primary"[^>]*icon=\{<SparklesIcon/],
];

describe("botoes de acao com icone nas telas da conta", () => {
  it.each(MAIN_ACTION)("%s", (file, pattern) => {
    expect(sources[file]).toMatch(pattern);
  });
});
