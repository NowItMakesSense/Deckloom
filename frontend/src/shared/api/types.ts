// Tipos compartilhados (design.md, "Tipos compartilhados"). Profile e SessionSummary nao tem email (REQ-013, REQ-045).
export type PresetAvatar = { kind: "preset"; presetId: string; imageUrl: string; alt: string };

export type CustomAvatar = { kind: "custom"; imageUrl: string; alt: string };

export type Avatar = PresetAvatar | CustomAvatar;

export type SessionSummary = {
  displayName: string;
  avatar: Avatar;
  hasPassword: boolean;
  hasGoogle: boolean;
};

export type ProfileFormat = { id: string; name: string };

export type Profile = {
  displayName: string;
  bio: string | null;
  favoriteTcg: { id: "mtg"; name: string };
  preferredFormats: ProfileFormat[];
  avatar: Avatar;
  hasPassword: boolean;
  hasGoogle: boolean;
};
