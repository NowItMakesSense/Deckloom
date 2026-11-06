import { useCallback, useEffect, useRef, useState } from "react";
import {
  useHeadingFocusOnTransition,
  useIsRouteTransition,
} from "@/shared/hooks/useHeadingFocusOnTransition";
import type { FormEvent, ReactNode } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { safeReturnTo } from "@/app/routes";
import { useApiClient, useRefreshSession } from "@/app/SessionProvider";
import { Alert } from "@/design-system/Alert";
import type { AlertVariant } from "@/design-system/Alert";
import { Button } from "@/design-system/Button";
import { Checkbox } from "@/design-system/Checkbox";
import { ErrorSummary } from "@/design-system/ErrorSummary";
import { FocusedAlert } from "@/design-system/FocusedAlert";
import { GoogleButton } from "@/design-system/GoogleButton";
import { startGoogleSignin } from "./googleSignin";
import { Link } from "@/design-system/Link";
import { PasswordField } from "@/design-system/PasswordField";
import { TextField } from "@/design-system/TextField";
import { ApiError } from "@/shared/api/apiClient";
import { useSubmit } from "@/shared/hooks/useSubmit";
import { messageFor } from "@/shared/messages/messages";
import "./pageShell.css";

const LOGIN_PATH = "/api/v1/auth/login";

// Textos proprios da S2: o mapa geral de messages.ts usa "Informe uma senha." (S1/S4/S5).
const EMAIL_REQUIRED = "Informe seu email.";
const PASSWORD_REQUIRED = "Informe sua senha.";
const INVALID_CREDENTIALS = "Email ou senha inválidos.";

const EMAIL_ID = "entrar-email";
const PASSWORD_ID = "entrar-senha";

interface Notice {
  variant: AlertVariant;
  text: string;
}

const NOTICES: Readonly<Record<string, Notice>> = {
  "sessao-expirada": { variant: "error", text: "Sua sessão expirou. Entre novamente." },
  saiu: { variant: "info", text: "Você saiu da sua conta." },
  "senha-alterada": { variant: "info", text: "Senha alterada. Entre com a nova senha." },
  "conta-excluida": { variant: "info", text: "Sua conta foi excluída." },
  "google-falhou": {
    variant: "info",
    text: "Não foi possível concluir o login com Google. Tente de novo ou entre com email e senha.",
  },
};

interface LoginValues {
  email: string;
  password: string;
  rememberMe: boolean;
}

interface FieldErrors {
  email?: string;
  password?: string;
}

function readValues(form: HTMLFormElement): LoginValues {
  const data = new FormData(form);
  const text = (name: string) => {
    const value = data.get(name);
    return typeof value === "string" ? value : "";
  };
  return {
    email: text("email").trim(),
    password: text("password"),
    rememberMe: data.has("rememberMe"),
  };
}

function validate(values: LoginValues): FieldErrors {
  const errors: FieldErrors = {};
  if (values.email === "") errors.email = EMAIL_REQUIRED;
  if (values.password === "") errors.password = PASSWORD_REQUIRED;
  return errors;
}

export function LoginPage(): ReactNode {
  const client = useApiClient();
  const refreshSession = useRefreshSession();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const valuesRef = useRef<LoginValues | null>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [summaryKey, setSummaryKey] = useState(0);
  const [passwordKey, setPasswordKey] = useState(0);
  const [rejections, setRejections] = useState(0);
  const [rateLimited, setRateLimited] = useState(false);

  const notice = NOTICES[searchParams.get("aviso") ?? ""];
  const returnTo = safeReturnTo(searchParams.get("retorno"));

  // Aviso de erro (sessao expirada) assume o foco inicial no lugar do titulo.
  const isTransition = useIsRouteTransition();
  const noticeTakesFocus = notice?.variant === "error";
  useHeadingFocusOnTransition(headingRef, !noticeTakesFocus);

  // Roda depois do foco do ErrorSummary: o primeiro campo invalido fica com o foco.
  const firstInvalidId = fieldErrors.email ? EMAIL_ID : fieldErrors.password ? PASSWORD_ID : null;
  useEffect(() => {
    if (summaryKey > 0 && firstInvalidId) document.getElementById(firstInvalidId)?.focus();
  }, [summaryKey, firstInvalidId]);

  const login = useCallback(async (): Promise<void> => {
    const values = valuesRef.current;
    if (!values) return;
    setRateLimited(false);
    try {
      await client.post(LOGIN_PATH, values);
    } catch (error) {
      // Rede, 5xx, atraso e outros codigos ficam para a T-108.
      if (error instanceof ApiError && error.code === "invalid_credentials") {
        setPasswordKey((key) => key + 1);
        setRejections((count) => count + 1);
      } else if (error instanceof ApiError && error.code === "too_many_attempts") {
        setRateLimited(true);
        setRejections(0);
      }
      return;
    }
    await refreshSession();
    navigate(returnTo, { replace: true });
  }, [client, navigate, refreshSession, returnTo]);

  const { status, submit } = useSubmit(login);
  const sending = status === "sending";

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const values = readValues(event.currentTarget);
    const found = validate(values);
    if (found.email || found.password) {
      setFieldErrors(found);
      setSummaryKey((key) => key + 1);
      return;
    }
    setFieldErrors({});
    setSummaryKey(0);
    valuesRef.current = values;
    void submit();
  }

  const summary = [
    ...(fieldErrors.email ? [{ id: EMAIL_ID, message: fieldErrors.email }] : []),
    ...(fieldErrors.password ? [{ id: PASSWORD_ID, message: fieldErrors.password }] : []),
  ];

  return (
    <main id="conteudo" tabIndex={-1} className="login-page">
      <h1 ref={headingRef} tabIndex={-1}>
        Entrar
      </h1>
      {rateLimited ? (
        <FocusedAlert variant="error">{messageFor("too_many_attempts")}</FocusedAlert>
      ) : rejections > 0 ? (
        <FocusedAlert key={rejections} variant="error">
          {INVALID_CREDENTIALS}
        </FocusedAlert>
      ) : notice?.variant === "error" ? (
        <FocusedAlert variant="error" focus={isTransition} announceAfterMount={!isTransition}>
          {notice.text}
        </FocusedAlert>
      ) : notice ? (
        <Alert variant={notice.variant}>{notice.text}</Alert>
      ) : null}
      <ErrorSummary key={summaryKey} errors={summary} />
      <form className="page-form" noValidate onSubmit={handleSubmit}>
        <TextField
          label="Email"
          name="email"
          id={EMAIL_ID}
          type="email"
          autoComplete="username"
          error={fieldErrors.email}
        />
        <PasswordField
          key={passwordKey}
          label="Senha"
          name="password"
          id={PASSWORD_ID}
          autoComplete="current-password"
          error={fieldErrors.password}
        />
        <Checkbox name="rememberMe" label="Lembrar-me neste dispositivo por 30 dias" />
        <Button type="submit" loading={sending}>
          Entrar
        </Button>
      </form>
      <p>
        <Link href="/esqueci-senha" standalone>
          Esqueci minha senha
        </Link>
      </p>
      <p aria-hidden="true">ou</p>
      <GoogleButton onClick={() => startGoogleSignin(searchParams.get("retorno"))} />
      <p>Criou sua conta com o Google? Use &quot;Continuar com Google&quot;.</p>
      <p>
        Não tem conta? <Link href="/cadastro">Criar conta</Link>
      </p>
    </main>
  );
}
