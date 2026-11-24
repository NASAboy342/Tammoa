import { createRouter, createWebHashHistory } from 'vue-router'

// Hash history: the desktop shell serves a static index.html.
export default createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', name: 'flows', component: () => import('@/views/FlowsView.vue') },
    { path: '/connections', name: 'connections', component: () => import('@/views/ConnectionsView.vue') },
    { path: '/settings', name: 'settings', component: () => import('@/views/SettingsView.vue') },
    { path: '/agent', name: 'agent', component: () => import('@/views/AgentAccessView.vue') },
    { path: '/flows/:id', name: 'flow-edit', component: () => import('@/views/FlowEditView.vue') },
  ],
})
