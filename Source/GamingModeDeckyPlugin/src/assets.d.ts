declare module "*.png" {
  const url: string;
  export default url;
}

declare module "*.jpg" {
  const url: string;
  export default url;
}
declare module "*.webp" {
  const url: string;
  export default url;
}
declare module "*.gif" {
  const value: string;
  export default value;
}

// Eternal Core: the optional Club module (src/club/), resolved by rollup.config.js; null when it is not in the tree.
declare module "core-optional:club" {
  const club: { ClubTab?: (props: { locale: string }) => any; clubAvailable?: () => boolean; label?: string } | null;
  export default club;
}
