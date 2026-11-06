import { useCallback, useEffect, useRef, useState } from "react";
import { useHeadingFocusOnTransition } from "@/shared/hooks/useHeadingFocusOnTransition";
import type { FormEvent, ReactNode } from "react";
import { useApiClient } from "@/app/SessionProvider";
import { Alert } from "@/design-system/Alert";
import { Button } from "@/design-system/Button";
import { ErrorSummary } from "@/design-system/ErrorSummary";
import { Link } from "@/design-system/Link";
import { TextField } from "@/design-system/TextField";
import { useSubmit } from "@/shared/hooks/useSubmit";
import { fieldMessage } from "@/shared/messages/messages";
import { validateEmail } from "@/shared/validation/validators";
import "./pageShell.css";

const REQUEST_PATH = "/api/v1/auth/password-reset/request";
const EMAIL_ID = "esqueci-senha-email";

// Mesma confirmacao para qualquer email valido: nao revela se a conta existe (REQ-032).
const UNIFORM_CONFIRMATION =
  "Se existir uma conta com esse email, enviaremos instruções para redefinir a senha. Confira também a caixa de spam.";
const SERVER_ERROR = "Não foi possível processar o pedido agora. Tente de novo.";
// S6 usa "Informe seu email." (texto da S2) para o campo vazio.
const EMAIL_REQUIRED = "Informe seu email.";

function emailError(email: string): string | undefined {
  const code = validateEmail(email);
  if (code === null) return undefined;
  return code === "required" ? EMAIL_REQUIRED : fieldMessage(code, "email");
}

export function ForgotPasswordPage(): ReactNode {
  const client = useApiClient();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const emailRef = useRef("");
  const [error, setError] = useState<string | undefined>();
  const [summaryKey, setSummaryKey] = useState(0);

  useHeadingFocusOnTransition(headingRef);

  // Roda depois do foco do ErrorSummary: o campo invalido fica com o foco.
  useEffect(() => {
    if (summaryKey > 0 && error) document.getElementById(EMAIL_ID)?.focus();
  }, [summaryKey, error]);

  const request = useCallback(async (): Promise<void> => {
    await client.post(REQUEST_PATH, { email: emailRef.current });
  }, [client]);

  const { status, submit } = useSubmit(request);
  const sending = status === "sending";

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const value = new FormData(event.currentTarget).get("email");
    const email = typeof value === "string" ? value.trim() : "";
    const found = emailError(email);
    if (found) {
      setError(found);
      setSummaryKey((key) => key + 1);
      return;
    }
    setError(undefined);
    setSummaryKey(0);
    emailRef.current = email;
    void submit();
  }

  return (
    <main id="conteudo" tabIndex={-1} className="forgot-password-page">
      <h1 ref={headingRef} tabIndex={-1}>
        Redefinir senha
      </h1>
      <p>
        Informe o email da sua conta. Se existir uma conta com ele, enviaremos instruções para
        redefinir a senha.
      </p>
      {status === "success" ? <Alert variant="success">{UNIFORM_CONFIRMATION}</Alert> : null}
      {status === "error" ? <Alert variant="error">{SERVER_ERROR}</Alert> : null}
      <ErrorSummary key={summaryKey} errors={error ? [{ id: EMAIL_ID, message: error }] : []} />
      <form className="page-form" noValidate onSubmit={handleSubmit}>
        <TextField
          label="Email"
          name="email"
          id={EMAIL_ID}
          type="email"
          autoComplete="username"
          error={error}
        />
        <Button type="submit" loading={sending}>
          Enviar instruções
        </Button>
      </form>
      <p>
        <Link href="/entrar" standalone>
          Voltar para entrar
        </Link>
      </p>
    </main>
  );
}
