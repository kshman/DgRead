-- 설정
CREATE TABLE IF NOT EXISTS configs (key TEXT PRIMARY KEY, value TEXT);

-- 파일 이동 정보
CREATE TABLE IF NOT EXISTS moves (no INTEGER PRIMARY KEY, alias TEXT, folder TEXT);

-- 읽은 책 이력
CREATE TABLE IF NOT EXISTS history (filename TEXT PRIMARY KEY, page INTEGER, updated TEXT);

-- 즐겨찾기
CREATE TABLE IF NOT EXISTS bookmarks (id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT, page INTEGER, created TEXT);
CREATE INDEX IF NOT EXISTS idx_bookmarks_path_page ON bookmarks(path, page);
