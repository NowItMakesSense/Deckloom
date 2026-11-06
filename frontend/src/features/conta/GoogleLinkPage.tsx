import { useCallback, useEffect, useRef, useState } from "react";
import { useHeadingFocusOnTransition } from "@/shared/hooks/useHeadingFocusOnTransition";
import type { FormEvent, ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient, useRefreshSession } from "@/app/SessionProvider";
import { Alert } from "@/design-system/Alert";
import { Button } from "@/design-system/Button";
import { ErrorSummary } from "@/design-system/ErrorSummary";
import { FormError } from "@/design-system/FormError";
import { Link } from "@/design-system/Link";
import { PasswordField } from "@/design-system/PasswordField";
import { Spinner } from "@/design-system/Spinner";
import { ApiError } from "@/shared/api/apiClient";
import { useSubmit } from "@/shared/hooks/useSubmit";
import { fieldMessage, messageFor } from "@/shared/messages/messages";
import { LIMITS } from "@/shared/validation/validators";
import "./pageShell.css";

const PENDING_PATH = "/api/v1/auth/google/pending";
const LINK_PATH = "/api/v1/auth/google/link";
const GOOGLE_FAILED = "/entrar?aviso=google-falhou";
const PASSWORD_ID = "vincular-senha";

// Textos proprios da S5 (a spec manda sobre o mapa geral de messages.ts).
const PASSWORD_REQUIRED = "Informe a senha da conta.";
const INCORRECT_PASSWORD = "Senha incorreta. O Google não foi vinculado.";
const SUCCESS_MESSAGE = "Google vinculado. Da próxima vez você pode entrar com o Google.";

// So o email e usado; subject e token nunca saem da resposta (REQ-006).
interface PendingInfo {
  email: string;
}

function validatePassword(password: string): string | undefined {
  if (password === "") return PASSWORD_REQUIRED;
  if (Array.from(password).length > LIMITS.passwordMax) {
    return fieldMessage("too_long", "password");
  }
  return undefined;
}

export function GoogleLinkPage(): ReactNode {
  const client = useApiClient();
  const refreshSession = useRefreshSession();
  const navigate = useNavigate();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const passwordRef = useRef("");
  const [pending, setPending] = useState<PendingInfo | null>(null);
  const [fieldError, setFieldError] = useState<string | undefined>();
  const [incorrect, setIncorrect] = useState(false);
  const [notCompleted, setNotCompleted] = useState(false);
  const [rateLimited, setRateLimited] = useState(false);
  const [summaryKey, setSummaryKey] = useState(0);
  const [passwordKey, setPasswordKey] = useState(0);

  useHeadingFocusOnTransition(headingRef);

  useEffect(() => {
    let active = true;
    client
      .get<PendingInfo>(PENDING_PATH)
      .then((info) => {
        if (active) setPending({ email: info.email });
      })
      .catch((error: unknown) => {
        // Demais falhas (rede, 5xx) ficam para a T-108.
        if (active && error instanceof ApiError && error.status === 404) {
          navigate(GOOGLE_FAILED, { replace: true });
        }
      });
    return () => {
      active = false;
    };
  }, [client, navigate]);

  // Campo invalido recebe o foco depois do ErrorSummary; senha incorreta devolve o foco ao campo limpo.
  useEffect(() => {
    if (summaryKey > 0 || passwordKey > 0) document.getElementById(PASSWORD_ID)?.focus();
  }, [summaryKey, passwordKey]);

  const link = useCallback(async (): Promise<void> => {
    try {
      await client.post(LINK_PATH, { password: passwordRef.current });
    } catch (error) {
      if (!(error instanceof ApiError)) return;
      if (error.status === 404) {
        navigate(GOOGLE_FAILED, { replace: true });
      } else if (error.code === "incorrect_password") {
        setIncorrect(true);
        setPasswordKey((key) => key + 1);
      } else if (error.code === "too_many_attempts") {
        setRateLimited(true);
      } else if (error.code === "google_not_completed") {
        setNotCompleted(true);
      } else if (error.fieldErrors.password?.[0]) {
        setFieldError(fieldMessage(error.fieldErrors.password[0], "password"));
        setSummaryKey((key) => key + 1);
      }
      return;
    }
    passwordRef.current = "";
    await refreshSession();
    navigate("/perfil", { replace: true, state: { message: SUCCESS_MESSAGE } });
  }, [client, navigate, refreshSession]);

  const { status, submit } = useSubmit(link);
  const sending = status === "sending";

  const cancel = useCallback(async (): Promise<void> => {
    try {
      await client.delete(PENDING_PATH);
    } catch {
      // A pendencia expira sozinha; a saida para a S2 nao depende da limpeza.
    }
    navigate("/entrar", { replace: true });
  }, [client, navigate]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const value = new FormData(event.currentTarget).get("password");
    const password = typeof value === "string" ? value : "";
    const found = validatePassword(password);
    setIncorrect(false);
    setNotCompleted(false);
    setRateLimited(false);
    setFieldError(found);
    if (found) {
      setSummaryKey((key) => key + 1);
      return;
    }
    setSummaryKey(0);
    passwordRef.current = password;
    void submit();
  }

  const summary = fieldError ? [{ id: PASSWORD_ID, message: fieldError }] : [];

  return (
    <main id="conteudo" tabIndex={-1} className="google-link-page">
      <h1 ref={headingRef} tabIndex={-1}>
        Vincular sua conta Google
      </h1>
      {pending === null ? (
        <div role="status">
          <Spinner label="Aguarde um instante..." />
        </div>
      ) : (
        <>
          <p className="page-readonly-email">Email do Google: {pending.email}</p>
          <p>Já existe uma conta com este email. Informe a senha dela para vincular o Google.</p>
          {rateLimited ? <Alert variant="error">{messageFor("too_many_attempts")}</Alert> : null}
          {incorrect ? <Alert variant="error">{INCORRECT_PASSWORD}</Alert> : null}
          <ErrorSummary key={summaryKey} errors={summary} />
          {notCompleted ? <FormError>{messageFor("google_not_completed")}</FormError> : null}
          <form className="page-form" noValidate onSubmit={handleSubmit}>
            <PasswordField
              key={passwordKey}
              label="Senha"
              name="password"
              id={PASSWORD_ID}
              autoComplete="current-password"
              error={fieldError}
            />
            <Button type="submit" loading={sending}>
              Vincular e entrar
            </Button>
          </form>
          <p>
            <Link href="/esqueci-senha" standalone>
              Esqueci minha senha
            </Link>
          </p>
          <Button variant="secondary" onClick={() => void cancel()}>
            Cancelar
          </Button>
        </>
      )}
    </main>
  );
}
