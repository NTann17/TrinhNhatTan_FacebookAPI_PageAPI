# Facebook Page API Backend (.NET + Swagger UI)

## 1) Chuan bi

### 1.1 Tao Facebook Page
- Tao 1 Page moi tren Facebook.
- Ghi lai thong tin:
  - Ten Page
  - Page ID
- Ket qua can nop:
  - Screenshot Page da tao (hien ten + ID)

### 1.2 Tao Facebook App tren Meta Developer
- Truy cap: https://developers.facebook.com/
- Tao App moi, gan Product phu hop (Facebook Login / Graph API).
- Ket qua can nop:
  - Screenshot dashboard app

### 1.3 Lay Page Access Token
- Dung Graph API Explorer hoac Business tools de cap token cho Page.
- Quyen (permissions) goi y:
  - pages_show_list
  - pages_read_engagement
  - pages_manage_posts
  - pages_read_user_content
- Ket qua can nop:
  - Screenshot token + danh sach permissions

## 2) API Backend

### Cong nghe
- Backend: ASP.NET Core Web API (.NET 9)
- API docs: Swagger UI

### Danh sach endpoint da co
- `GET /api/page/{pageId}`
- `GET /api/page/{pageId}/posts`
- `POST /api/page/{pageId}/posts`
- `DELETE /api/page/post/{postId}`
- `GET /api/page/post/{postId}/comments`
- `GET /api/page/post/{postId}/likes`
- `GET /api/page/{pageId}/insights`

### Cau hinh token
Cap nhat `appsettings.json`:

```json
"Facebook": {
  "GraphApiBaseUrl": "https://graph.facebook.com",
  "GraphApiVersion": "v22.0",
  "PageAccessToken": "YOUR_PAGE_ACCESS_TOKEN",
  "UserAccessToken": "YOUR_USER_ACCESS_TOKEN",
  "AppId": "YOUR_META_APP_ID",
  "AppSecret": "YOUR_META_APP_SECRET"
}
```

Neu `POST /api/page/{pageId}/posts` bi 403, kiem tra lai:
- Token phai la token cua Page hoac User co quyen truy cap Page.
- Page admin phai co quyen quan tri du hop le tren Page.
- Can co `pages_read_engagement` va `pages_manage_posts`.
- Neu dung `UserAccessToken`, app se thu lay Page access token tu `GET /me/accounts`.
- Neu dien `AppId` va `AppSecret`, API se goi `debug_token` de hien scopes va trang thai token trong loi tra ve.

### Chay project
```bash
dotnet restore
dotnet run
```

Swagger UI:
- `https://localhost:<port>/swagger`
- Hoac `http://localhost:<port>/swagger`
