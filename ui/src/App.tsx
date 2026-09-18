import { Route, Routes } from 'react-router'
import { NewSubmainPage } from './routes/NewSubmainPage'
import { PanelPage } from './routes/PanelPage'
import { ProjectPage } from './routes/ProjectPage'
import { ProjectsPage } from './routes/ProjectsPage'

export function App() {
  return (
    <Routes>
      <Route path="/" element={<ProjectsPage />} />
      <Route path="/projects/:projectId" element={<ProjectPage />} />
      <Route path="/projects/:projectId/submains/new" element={<NewSubmainPage />} />
      <Route path="/submains/:submainId/panel" element={<PanelPage />} />
    </Routes>
  )
}
