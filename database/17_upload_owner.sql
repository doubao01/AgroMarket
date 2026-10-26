-- 17. 上传文件归属登记
-- ---------------------------------------------------------------
-- 上传接口此前不记录"谁传了这个文件": 于是一旦某个 /uploads 地址流出去(聊天里发图、
-- 管理端回显资质材料), 既无法判断它属于谁, 也无法在用户更换头像/删除账号时安全清理,
-- 更不能给"删除自己的文件"提供授权依据(没有归属就只剩"谁都能删"或"谁都删不了")。
-- 这里只登记元信息, 不存文件本身; 唯一键 url 保证同一地址不会被重复登记。
CREATE TABLE IF NOT EXISTS sys_upload (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id    BIGINT UNSIGNED NOT NULL COMMENT '上传者',
  url        VARCHAR(255) NOT NULL COMMENT '对外地址 /uploads/yyyyMMdd/<32位>.<ext>',
  size_bytes BIGINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '净化后大小',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_su_url (url),
  KEY idx_su_user (user_id, created_at)
) ENGINE=InnoDB COMMENT='上传文件归属登记';
