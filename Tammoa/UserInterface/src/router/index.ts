import { createRouter, createWebHashHistory } from 'vue-router'

// Hash history: the desktop shell serves a static index.html.
export default createRouter({
  history: createWebHashHistory(),
  routes: [{ path: '/', name: 'home', component: () => import('@/views/HomeView.vue') }],
})
