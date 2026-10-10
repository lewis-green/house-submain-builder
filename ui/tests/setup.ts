import '@testing-library/jest-dom/vitest'

// jsdom's own window.scrollTo only logs "not implemented"; the sheet's scroll
// lock calls it on every close.
window.scrollTo = () => {}
