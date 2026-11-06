import { useEffect, useId, useState } from "react";
import type { ReactNode } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { Alert } from "@/design-system/Alert";
import { Avatar } from "@/design-system/Avatar";
import { Button } from "@/design-system/Button";
import { Skeleton } from "@/design-system/Skeleton";
import { Tag } from "@/design-system/Tag";
import { messageFor } from "@/shared/messages/messages";
import { AvatarDialog } from "./AvatarDialog";
import type { AvatarValue } from "./AvatarDialog";
import { ConfirmAccountDeletionDialog } from "./ConfirmAccountDeletionDialog";
import { DeleteAccountDialog } from "./DeleteAccountDialog";
import { DeleteAccountGoogleDialog } from "./DeleteAccountGoogleDialog";
import { useProfile } from "./useProfile";
import type { Profile } from "@/shared/api";

import "./ProfilePage.css";

const AVATAR_SIZE = 96;
const SLOW_LOAD_MS = 2000;
const BIO_CLAMP_LINES = 10;
// Sem medir o layout: bio com muitas linhas ou longa o bastante para passar do limite de linhas.
const BIO_LONG_CHARS = 200;
const EDIT_PATH = "/perfil/editar";

const LOAD_ERROR = "Não foi possível carregar seu perfil.";
const DELETE_FAILED = "Não foi possível confirmar sua identidade. Sua conta não foi excluída.";

type Exclusion = "start" | "reauth" | "confirm" | "failed" | null;

function exclusionFrom(value: string | null): Exclusion {
  if (value === "confirmar") return "confirm";
  return value === "falhou" ? "failed" : null;
}

function isLongBio(bio: string): boolean {
  return bio.split("\n").length > BIO_CLAMP_LINES || bio.length > BIO_LONG_CHARS;
}

function successMessage(state: unknown): string | null {
  if (typeof state !== "object" || state === null || !("message" in state)) return null;
  return typeof state.message === "string" && state.message !== "" ? state.message : null;
}

function LoadingBlock(): ReactNode {
  const [slow, setSlow] = useState(false);
  useEffect(() => {
    const timer = setTimeout(() => setSlow(true), SLOW_LOAD_MS);
    return () => clearTimeout(timer);
  }, []);
  return (
    <div className="profile__loading">
      <Skeleton />
      <Skeleton />
      <Skeleton />
      {slow ? <p role="status">Carregando...</p> : null}
    </div>
  );
}

function Bio({ bio }: { bio: string }): ReactNode {
  const [expanded, setExpanded] = useState(false);
  const long = isLongBio(bio);
  return (
    <>
      <p
        dir="auto"
        className={long && !expanded ? "profile__bio profile__bio--clamped" : "profile__bio"}
      >
        {bio}
      </p>
      {long ? (
        <button
          type="button"
          className="profile__toggle"
          aria-expanded={expanded}
          onClick={() => setExpanded((value) => !value)}
        >
          {expanded ? "Mostrar menos" : "Mostrar tudo"}
        </button>
      ) : null}
    </>
  );
}

function ProfileDetails({ profile, avatar }: { profile: Profile; avatar: AvatarValue }): ReactNode {
  return (
    <>
      <div className="profile__identity">
        <Avatar
          src={avatar.imageUrl}
          custom={avatar.kind === "custom"}
          name={profile.displayName}
          size={AVATAR_SIZE}
          decorative
        />
        <p dir="auto" className="profile__name">
          {profile.displayName}
        </p>
      </div>
      <dl className="profile__fields">
        <dt>TCG favorito</dt>
        <dd>{profile.favoriteTcg.name}</dd>
        <dt>Formatos preferidos</dt>
        <dd>
          {profile.preferredFormats.length === 0 ? (
            <span className="profile__empty">Nenhum formato selecionado.</span>
          ) : (
            <ul className="profile__formats">
              {profile.preferredFormats.map((format) => (
                <li key={format.id}>
                  <Tag>{format.name}</Tag>
                </li>
              ))}
            </ul>
          )}
        </dd>
        <dt>Bio</dt>
        <dd>
          {profile.bio === null || profile.bio === "" ? (
            <span className="profile__empty">Nenhuma bio.</span>
          ) : (
            <Bio bio={profile.bio} />
          )}
        </dd>
      </dl>
    </>
  );
}

export function ProfilePage(): ReactNode {
  const { state, retry } = useProfile();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const [exclusion, setExclusion] = useState<Exclusion>(() =>
    exclusionFrom(searchParams.get("exclusao")),
  );
  const [reauthNotice, setReauthNotice] = useState("");
  const [avatarOpen, setAvatarOpen] = useState(false);
  // Avatar trocado pelo dialogo nesta visita; sem cache: uma nova entrada na tela le o servidor de novo.
  const [changedAvatar, setChangedAvatar] = useState<AvatarValue | null>(null);
  const closeDialogs = () => setExclusion(null);
  const dangerTitleId = useId();
  const message = successMessage(location.state);

  // O resultado da reautenticacao (S3) e lido na entrada; o parametro sai da URL (REQ-044).
  useEffect(() => {
    if (!searchParams.has("exclusao")) return;
    const next = new URLSearchParams(searchParams);
    next.delete("exclusao");
    setSearchParams(next, { replace: true });
  }, [searchParams, setSearchParams]);

  return (
    <main id="conteudo" tabIndex={-1} className="profile">
      <h1>Meu perfil</h1>
      {message ? <Alert variant="success">{message}</Alert> : null}
      {exclusion === "failed" ? <Alert variant="error">{DELETE_FAILED}</Alert> : null}
      {state.status === "loading" ? <LoadingBlock /> : null}
      {state.status === "error" ? (
        <>
          <Alert variant="error">{LOAD_ERROR}</Alert>
          <Button onClick={retry}>Tentar de novo</Button>
        </>
      ) : null}
      {state.status === "ready" ? (
        <>
          <ProfileDetails profile={state.profile} avatar={changedAvatar ?? state.profile.avatar} />
          <div className="profile__actions">
            <Button variant="primary" onClick={() => navigate(EDIT_PATH)}>
              Editar perfil
            </Button>
            <Button variant="secondary" onClick={() => setAvatarOpen(true)}>
              Alterar avatar
            </Button>
          </div>
          <AvatarDialog
            open={avatarOpen}
            onClose={() => setAvatarOpen(false)}
            current={changedAvatar ?? state.profile.avatar}
            onChanged={setChangedAvatar}
          />
          <section className="profile__danger" aria-labelledby={dangerTitleId}>
            <h2 id={dangerTitleId}>Zona de perigo</h2>
            <Button variant="danger" onClick={() => setExclusion("start")}>
              Excluir conta
            </Button>
          </section>
        </>
      ) : null}
      {/* Unico ponto de escolha do ramo: hasPassword do perfil (design.md S11). */}
      {state.status === "ready" && state.profile.hasPassword ? (
        <DeleteAccountDialog open={exclusion === "start"} onClose={closeDialogs} />
      ) : (
        <DeleteAccountGoogleDialog
          open={exclusion === "start" || exclusion === "reauth"}
          onClose={closeDialogs}
          notice={exclusion === "reauth" ? reauthNotice : undefined}
        />
      )}
      <ConfirmAccountDeletionDialog
        open={exclusion === "confirm"}
        onClose={closeDialogs}
        onReauthNeeded={(code) => {
          setReauthNotice(messageFor(code));
          setExclusion("reauth");
        }}
      />
    </main>
  );
}
