# ピピピ技研 Design System

ユーザー提供の「ピピピ技研 Design System.zip」から、配布ページで使用する配色・書体・余白・枠線・影のトークンとフォントを収録しています。

- `tokens/colors.css`、`typography.css`、`spacing.css`、`effects.css` は提供データのままです。
- `tokens/fonts.css` は使用するウェイト（Space Grotesk 300–700、Zen Kaku Gothic New 400・500・900）のみ収録しています。
- ボタンは提供された `components/core/Button.jsx` の large / primary・outline の寸法とトークンを静的HTML/CSSへ適用しています。
- 元デザインシステムのコンポーネント形状の出典：Neo Brutalism UI Library (Community)、basma.design。ブランド配色・フォントは提供デザインシステムに基づきます。

サイトの内容は `../index.html`、レイアウトと状態表現は `../styles.css`、コピー操作は `../app.js` で管理します。
