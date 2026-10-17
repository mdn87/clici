import type { Register } from 'claude-code';

// No Clici state, message hooks, text changes, file writes, or keyboard handlers.
export const register: Register = on => {
    on('ui.render', { component: 'UserMessage' }, (_$, e, next) => next(e));
};
