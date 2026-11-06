import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient, useRefreshSession } from "@/app/SessionProvider";
import { Alert } from "@/design-system/Alert";
import { Avatar } from "@/design-system/Avatar";
import { Button } from "@/design-system/Button";
import { CheckboxGroup } from "@/design-system/CheckboxGroup";
import type { ChoiceOption } from "@/design-system/CheckboxGroup";
import { ErrorSummary } from "@/design-system/ErrorSummary";
import { Select } from "@/design-system/Select";
import { Skeleton } from "@/design-system/Skeleton";
import { Textarea } from "@/design-system/Textarea";
import { TextField } from "@/design-system/TextField";
import { ApiError } from "@/shared/api/apiClient";
import { useSubmit } from "@/shared/hooks/useSubmit";
import { fieldMessage } from "@/shared/messages/messages";
import { validateBio, validateDisplayName } from "@/shared/validation/validators";
import { AvatarDialog } from "./AvatarDialog";
import type { AvatarValue } from "./AvatarDialog";
import { useProfile } from "./useProfile";
import type { Profile } from "@/shared/api";

import "./EditProfilePage.css";
import "./pageShell.css";

const AVATAR_SIZE = 96;
const PROFILE_PATH = "/api/v1/me/profile";
const FORMATS_PATH = "/api/v1/catalog/formats";
const TCGS_PATH = "/api/v1/catalog/favorite-tcgs";
const PROFILE_ROUTE = "/perfil";

const LOAD_ERROR = "Não foi possível carregar seu perfil.";
const SAVE_ERROR = "Não foi possível salvar. Tente de novo.";
const NO_FORMATS = "Nenhum formato disponível no momento.";
const SUCCESS_MESSAGE = "Perfil atualizado.";

interface CatalogItem {
  id: string;
  name: string;
}

type Catalog =
  { status: "loading" } | { status: "failed" } | { status: "ready"; items: CatalogItem[] };

interface FormValues {
  displayName: string;
  bio: string;
  favoriteTcg: string;
  formatIds: string[];
}

type Field = keyof FormValues;
type FieldCodes = Partial<Record<Field, string>>;

const FIELD_ORDER: readonly Field[] = ["displayName", "bio", "favoriteTcg", "formatIds"];

const FIELD_IDS: Readonly<Record<Field, string>> = {
  displayName: "perfil-nome",
  bio: "perfil-bio",
  favoriteTcg: "perfil-tcg",
  formatIds: "perfil-formatos",
};

function useCatalog(path: string): Catalog {
  const client = useApiClient();
  const [catalog, setCatalog] = useState<Catalog>({ status: "loading" });
  useEffect(() => {
    let active = true;
    client
      .get<CatalogItem[]>(path)
      .then((items) => {
        if (active) setCatalog({ status: "ready", items });
      })
      .catch(() => {
        if (active) setCatalog({ status: "failed" });
      });
    return () => {
      active = false;
    };
  }, [client, path]);
  return catalog;
}

function toOptions(items: CatalogItem[]): ChoiceOption[] {
  return items.map((item) => ({ value: item.id, label: item.name }));
}

function readValues(form: HTMLFormElement): FormValues {
  const data = new FormData(form);
  const text = (key: string): string => {
    const value = data.get(key);
    return typeof value === "string" ? value : "";
  };
  return {
    displayName: text("displayName"),
    bio: text("bio"),
    favoriteTcg: text("favoriteTcg"),
    formatIds: data.getAll("formatIds").filter((id): id is string => typeof id === "string"),
  };
}

function validate(values: FormValues): FieldCodes {
  const codes: FieldCodes = {};
  const displayName = validateDisplayName(values.displayName);
  if (displayName) codes.displayName = displayName;
  const bio = validateBio(values.bio);
  if (bio) codes.bio = bio;
  return codes;
}

// Traduz o problema do servidor em um codigo por campo; texto do servidor nunca e usado.
function serverFieldCodes(error: ApiError): FieldCodes {
  const codes: FieldCodes = {};
  for (const field of FIELD_ORDER) {
    const first = error.fieldErrors[field]?.[0];
    if (first) codes[field] = first;
  }
  return codes;
}

function FormatsLoading(): ReactNode {
  return (
    <fieldset className="choice-group edit-profile__formats-loading">
      <legend className="choice-group__legend">Formatos preferidos</legend>
      <Skeleton />
    </fieldset>
  );
}

interface FormProps {
  profile: Profile;
  tcgs: Catalog;
  formats: Catalog;
}

function EditForm({ profile, tcgs, formats }: FormProps): ReactNode {
  const client = useApiClient();
  const refreshSession = useRefreshSession();
  const navigate = useNavigate();
  const valuesRef = useRef<FormValues | null>(null);
  const [codes, setCodes] = useState<FieldCodes>({});
  const [saveFailed, setSaveFailed] = useState(false);
  const [summaryKey, setSummaryKey] = useState(0);
  const [avatarOpen, setAvatarOpen] = useState(false);
  const [avatar, setAvatar] = useState<AvatarValue>(profile.avatar);

  const showErrors = useCallback((next: FieldCodes): void => {
    setCodes(next);
    setSummaryKey((key) => key + 1);
  }, []);

  const save = useCallback(async (): Promise<void> => {
    const values = valuesRef.current;
    if (!values) return;
    // PENDENTE de confirmacao do usuario: com o catalogo de formatos indisponivel ou vazio,
    // reenvia a selecao carregada do perfil em vez de enviar formatIds vazio.
    const catalogUsable = formats.status === "ready" && formats.items.length > 0;
    const formatIds = catalogUsable
      ? values.formatIds
      : profile.preferredFormats.map((format) => format.id);
    try {
      await client.put(PROFILE_PATH, {
        displayName: values.displayName,
        bio: values.bio === "" ? null : values.bio,
        favoriteTcg: values.favoriteTcg,
        formatIds,
      });
    } catch (error) {
      // 401: o cliente de API ja derrubou a sessao e o guard leva a S2.
      if (error instanceof ApiError && error.status === 401) return;
      if (error instanceof ApiError && error.status === 400) {
        showErrors(serverFieldCodes(error));
      } else {
        setSaveFailed(true);
      }
      return;
    }
    await refreshSession();
    navigate(PROFILE_ROUTE, { state: { message: SUCCESS_MESSAGE } });
  }, [client, formats, navigate, profile, refreshSession, showErrors]);

  const { status, submit } = useSubmit(save);
  const sending = status === "sending";

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const values = readValues(event.currentTarget);
    const found = validate(values);
    if (Object.keys(found).length > 0) {
      showErrors(found);
      return;
    }
    setCodes({});
    setSaveFailed(false);
    valuesRef.current = values;
    void submit();
  }

  const messageOf = (field: Field): string | undefined => {
    const code = codes[field];
    return code ? fieldMessage(code, field) : undefined;
  };

  const summary = FIELD_ORDER.filter((field) => codes[field]).map((field) => ({
    id: FIELD_IDS[field],
    message: messageOf(field) ?? "",
  }));

  const tcgItems = tcgs.status === "ready" ? tcgs.items : [profile.favoriteTcg];
  const formatOptions = formats.status === "ready" ? toOptions(formats.items) : [];

  return (
    <>
      <div className="edit-profile__identity">
        <Avatar
          src={avatar.imageUrl}
          custom={avatar.kind === "custom"}
          name={profile.displayName}
          size={AVATAR_SIZE}
          decorative
        />
        <Button variant="secondary" onClick={() => setAvatarOpen(true)}>
          Alterar avatar
        </Button>
      </div>
      <ErrorSummary key={summaryKey} errors={summary} />
      {saveFailed ? <Alert variant="error">{SAVE_ERROR}</Alert> : null}
      <form noValidate className="edit-profile__form" onSubmit={handleSubmit}>
        <TextField
          label="Nome de exibição"
          name="displayName"
          id={FIELD_IDS.displayName}
          autoComplete="nickname"
          defaultValue={profile.displayName}
          hint="Dica: de 3 a 30 caracteres. Outros Jogadores podem ter o mesmo nome."
          error={messageOf("displayName")}
        />
        <Textarea
          label="Bio"
          name="bio"
          id={FIELD_IDS.bio}
          defaultValue={profile.bio ?? ""}
          error={messageOf("bio")}
        />
        <Select
          key={tcgs.status}
          label="TCG favorito"
          name="favoriteTcg"
          id={FIELD_IDS.favoriteTcg}
          options={toOptions(tcgItems)}
          defaultValue={profile.favoriteTcg.id}
          error={messageOf("favoriteTcg")}
        />
        <div id={FIELD_IDS.formatIds} tabIndex={-1} className="edit-profile__formats">
          {formats.status === "loading" ? (
            <FormatsLoading />
          ) : (
            <CheckboxGroup
              legend="Formatos preferidos"
              name="formatIds"
              options={formatOptions}
              defaultValue={profile.preferredFormats.map((format) => format.id)}
              emptyText={NO_FORMATS}
              error={messageOf("formatIds")}
            />
          )}
        </div>
        <div className="edit-profile__actions">
          <Button type="submit" variant="primary" loading={sending}>
            Salvar alterações
          </Button>
          <Button variant="secondary" onClick={() => navigate(PROFILE_ROUTE)}>
            Cancelar
          </Button>
        </div>
      </form>
      <AvatarDialog
        open={avatarOpen}
        onClose={() => setAvatarOpen(false)}
        current={avatar}
        onChanged={setAvatar}
      />
    </>
  );
}

export function EditProfilePage(): ReactNode {
  const { state, retry } = useProfile();
  const tcgs = useCatalog(TCGS_PATH);
  const formats = useCatalog(FORMATS_PATH);

  return (
    <main id="conteudo" tabIndex={-1} className="edit-profile">
      <h1>Editar perfil</h1>
      {state.status === "loading" ? (
        <div className="edit-profile__loading">
          <Skeleton />
          <Skeleton />
          <Skeleton />
        </div>
      ) : null}
      {state.status === "error" ? (
        <>
          <Alert variant="error">{LOAD_ERROR}</Alert>
          <Button onClick={retry}>Tentar de novo</Button>
        </>
      ) : null}
      {state.status === "ready" ? (
        <EditForm profile={state.profile} tcgs={tcgs} formats={formats} />
      ) : null}
    </main>
  );
}
