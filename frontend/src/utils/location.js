const STORAGE_KEY = 'current_location'

export function getLocation() {
  try {
    const v = uni.getStorageSync(STORAGE_KEY)
    if (v && typeof v === 'object' && v.townId) return v
  } catch (e) {}
  return null
}

export function setLocation(loc) {
  try {
    if (loc && loc.townId) uni.setStorageSync(STORAGE_KEY, loc)
    else uni.removeStorageSync(STORAGE_KEY)
  } catch (e) {}
}

export function getTownLabel(loc) {
  return loc && loc.townName ? loc.townName : '全部乡镇'
}

export function sameLocation(a, b) {
  const at = a ? a.townId || null : null
  const bt = b ? b.townId || null : null
  const av = a ? a.villageId || null : null
  const bv = b ? b.villageId || null : null
  return at === bt && av === bv
}
