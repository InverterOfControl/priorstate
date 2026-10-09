import DefaultTheme from 'vitepress/theme'
import { h } from 'vue'
import StartPaths from './StartPaths.vue'
import './style.css'

export default {
  extends: DefaultTheme,
  Layout: () => h(DefaultTheme.Layout, null, {
    'home-hero-after': () => h(StartPaths),
  }),
}
