import { useEffect, useId, useState } from "react";
import type { ChangeEvent, ReactNode } from "react";
import { useApiClient, useRefreshSession } from "@/app/SessionProvider";
import { Alert } from "@/design-system/Alert";
import { Button } from "@/design-system/Button";
import { Dialog } from "@/design-system/Dialog";
import { FormError } from "@/design-system/FormError";
import { Skeleton } from "@/design-system/Skeleton";
import { ApiError } from "@/shared/api/apiClient";

import "./AvatarDialog.css";

export interface AvatarValue {
  kind: "preset" | "custom";
  presetId?: string;
  imageUrl: string;
  alt: string;
}

export interface AvatarDialogProps {
  open: boolean;
  onClose: () => void;
  current: AvatarValue;
  onChanged?: (avatar: AvatarValue) => void;
}

interface Preset {
  id: string;
  label: string;
  imageUrl: string;
}

type Presets = { status: "loading" } | { status: "failed" } | { status: "ready"; items: Preset[] };

const PRESETS_PATH = "/api/v1/catalog/avatar-presets";
const PRESET_PATH = "/api/v1/me/avatar/preset";
const CUSTOM_PATH = "/api/v1/me/avatar/custom";

const LEGEND = "Escolha um avatar";
const SKELETON_COUNT = 8;
const MAX_BYTES = 2_097_152;
const ACCEPTED_TYPES = ["image/jpeg", "image/png", "image/webp"];

const LOAD_PRESETS = "Não foi possível carregar os avatares. Tente de novo.";
const SAVE_ERROR = "Não foi possível salvar. Tente de novo.";
const REJECTED =
  "Não foi possível usar este arquivo. Envie uma imagem JPEG, PNG ou WebP de até 2 MB.";
const NETWORK = "Não foi possível enviar agora. Tente de novo.";
const NO_FILE = "Escolha um arquivo para enviar.";
const BAD_TYPE = "Escolha uma imagem JPEG, PNG ou WebP.";
const TOO_BIG = "A imagem precisa ter no máximo 2 MB.";
const SENDING = "Enviando imagem...";
const UPDATED = "Avatar atualizado.";
const HINT = "JPEG, PNG ou WebP, até 2 MB. Removemos os metadados da imagem.";

function validateFile(file: File | undefined): string | undefined {
  if (!file) return NO_FILE;
  if (!ACCEPTED_TYPES.includes(file.type)) return BAD_TYPE;
  if (file.size > MAX_BYTES) return TOO_BIG;
  return undefined;
}

function uploadErrorMessage(error: unknown): string {
  return error instanceof ApiError && error.code === "avatar_rejected" ? REJECTED : NETWORK;
}

function usePresets(): Presets {
  const client = useApiClient();
  const [presets, setPresets] = useState<Presets>({ status: "loading" });
  useEffect(() => {
    let active = true;
    client
      .get<Preset[]>(PRESETS_PATH)
      .then((items) => {
        if (active)
          setPresets(items.length > 0 ? { status: "ready", items } : { status: "failed" });
      })
      .catch(() => {
        if (active) setPresets({ status: "failed" });
      });
    return () => {
      active = false;
    };
  }, [client]);
  return presets;
}

function PresetSkeletons(): ReactNode {
  return (
    <div className="avatar-picker__grid" aria-busy="true">
      {Array.from({ length: SKELETON_COUNT }, (_, index) => (
        <div className="avatar-picker__skeleton" key={index}>
          <Skeleton />
        </div>
      ))}
    </div>
  );
}

interface BodyProps {
  onClose: () => void;
  current: AvatarValue;
  onChanged?: (avatar: AvatarValue) => void;
  onUpdated: () => void;
}

// Montado so enquanto aberto: cada abertura recomeca do estado inicial e le o catalogo de novo (sem cache).
function AvatarDialogBody({ onClose, current, onChanged, onUpdated }: BodyProps): ReactNode {
  const client = useApiClient();
  const refreshSession = useRefreshSession();
  const presets = usePresets();
  const groupName = useId();
  const fileId = useId();
  const hintId = `${fileId}-hint`;
  const errorId = `${fileId}-error`;
  const [selectedId, setSelectedId] = useState<string | undefined>(
    current.kind === "preset" ? current.presetId : undefined,
  );
  const [file, setFile] = useState<File | undefined>();
  const [fileError, setFileError] = useState<string | undefined>();
  const [presetError, setPresetError] = useState(false);
  const [sending, setSending] = useState<"preset" | "upload" | undefined>();

  async function apply(kind: "preset" | "upload", request: () => Promise<{ avatar: AvatarValue }>) {
    setSending(kind);
    try {
      const { avatar } = await request();
      onUpdated();
      onChanged?.(avatar);
      onClose();
      void refreshSession();
    } catch (error) {
      if (kind === "preset") setPresetError(true);
      else setFileError(uploadErrorMessage(error));
    } finally {
      setSending(undefined);
    }
  }

  function submitPreset() {
    if (!selectedId || sending) return;
    setPresetError(false);
    void apply("preset", () => client.put(PRESET_PATH, { presetId: selectedId }));
  }

  function upload() {
    if (sending) return;
    const problem = validateFile(file);
    setFileError(problem);
    if (problem || !file) return;
    const body = new FormData();
    body.append("file", file);
    void apply("upload", () => client.upload(CUSTOM_PATH, body));
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    setFile(event.target.files?.[0]);
    setFileError(undefined);
  }

  return (
    <Dialog open onClose={onClose} title="Alterar avatar" busy={sending !== undefined}>
      <div className="avatar-dialog">
        <fieldset className="avatar-dialog__presets">
          <legend className="avatar-picker__legend">{LEGEND}</legend>
          {presets.status === "loading" ? <PresetSkeletons /> : null}
          {presets.status === "failed" ? (
            <p className="avatar-picker__message">{LOAD_PRESETS}</p>
          ) : null}
          {presets.status === "ready" ? (
            <div className="avatar-picker__grid">
              {presets.items.map((preset) => (
                <label className="avatar-picker__option" key={preset.id}>
                  <input
                    className="avatar-picker__input"
                    type="radio"
                    name={groupName}
                    value={preset.id}
                    aria-label={preset.label}
                    checked={selectedId === preset.id}
                    onChange={() => setSelectedId(preset.id)}
                  />
                  <img className="avatar-picker__image" src={preset.imageUrl} alt="" />
                </label>
              ))}
            </div>
          ) : null}
        </fieldset>
        {presetError ? <Alert variant="error">{SAVE_ERROR}</Alert> : null}
        {presets.status === "ready" ? (
          <Button
            variant="primary"
            loading={sending === "preset"}
            onClick={submitPreset}
            disabled={!selectedId}
          >
            Usar este avatar
          </Button>
        ) : null}

        <div className="avatar-dialog__upload">
          <label className="file-upload__label" htmlFor={fileId}>
            Arquivo
          </label>
          <input
            id={fileId}
            className="file-upload__input"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            aria-describedby={fileError ? `${hintId} ${errorId}` : hintId}
            onChange={handleFileChange}
          />
          <p className="file-upload__hint" id={hintId}>
            {HINT}
          </p>
          {file ? <p className="file-upload__name">{file.name}</p> : null}
          {fileError ? <FormError id={errorId}>{fileError}</FormError> : null}
          {sending === "upload" ? <p role="status">{SENDING}</p> : null}
          <Button variant="secondary" loading={sending === "upload"} onClick={upload}>
            Enviar imagem
          </Button>
        </div>

        <div className="avatar-dialog__actions">
          <Button variant="secondary" onClick={sending ? undefined : onClose}>
            Fechar
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

export function AvatarDialog({ open, onClose, current, onChanged }: AvatarDialogProps): ReactNode {
  const [updated, setUpdated] = useState(false);
  const [wasOpen, setWasOpen] = useState(open);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) setUpdated(false);
  }
  return (
    <>
      {open ? (
        <AvatarDialogBody
          onClose={onClose}
          current={current}
          onChanged={onChanged}
          onUpdated={() => setUpdated(true)}
        />
      ) : null}
      {updated ? <p role="status">{UPDATED}</p> : null}
    </>
  );
}
